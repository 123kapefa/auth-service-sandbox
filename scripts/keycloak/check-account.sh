#!/usr/bin/env bash
# Проверка этапа 6б (docs/keycloak/06b-account-branding.md): личный кабинет /account.
# Пароль и почта меняются на страницах Keycloak (тема проекта) по запросу из кабинета (/auth/login?action=…),
# итог возвращается в кабинет (action_status). Проверка заводит своего пользователя и удаляет его в конце.
# Запуск: стенд поднят (Docker: сервис :8080, Keycloak :8180, Mailpit :8025), затем
#   scripts/keycloak/check-account.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
MAILPIT="${MAILPIT:-http://localhost:${MAILPIT_UI_PORT:-8025}}"
REALM=sandbox
PHONE=79990000099
OTHER_PHONE=79990000098
EMAIL=account-check@sandbox.test
NEW_EMAIL=account-check-new@sandbox.test
NEW_PASSWORD='Changed1234!'

MASTER=$(master_token)
admin_api() { curl -s -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' "$@"; }
# user_id [телефон] — Id пользователя проверки (по умолчанию — основного)
user_id() { admin_api "$KC/admin/realms/$REALM/users?username=${1:-$PHONE}&exact=true" | python3 -c 'import json,sys; u=json.load(sys.stdin); print(u[0]["id"] if u else "")'; }
remove_user() { local id; id=$(user_id "${1:-$PHONE}"); [ -z "$id" ] || admin_api -X DELETE "$KC/admin/realms/$REALM/users/$id" > /dev/null; }
# create_user <телефон> [почта] — пользователь проверки с паролем $PASSWORD. JSON — заранее, не внутри "$(…)": bash 3.2
# (macOS) раскрывает там фигурные скобки и рвёт тело на части.
create_user() {
  local json
  json=$(python3 -c 'import json,sys; u={"username": sys.argv[1], "enabled": True, "attributes": {"phone": ["+" + sys.argv[1]]},
  "credentials": [{"type": "password", "value": sys.argv[2], "temporary": False}]}
if sys.argv[3]: u.update(email=sys.argv[3], emailVerified=True)
print(json.dumps(u))' "$1" "$PASSWORD" "${2:-}")
  admin_api -o /dev/null -w '%{http_code}' -d "$json" "$KC/admin/realms/$REALM/users"
}
trap 'remove_user; remove_user "$OTHER_PHONE"; rm -rf "$WORK"' EXIT

# aia <jar> <действие> [Id учётных данных] — вход с действием из кабинета; страница действия Keycloak — в
# $WORK/page.html (Keycloak сначала переадресует на login-actions/required-action — идём по его переадресациям).
aia() {
  local h="$WORK/h" url
  curl -s -o /dev/null -D "$h" -b "$1" -c "$1" "$API/auth/login?return_to=/account/?action=$2&action=$2${3:+&credential=$3}"
  url=$(header "$h" location)
  for _ in 1 2 3 4 5; do
    curl -s -o "$WORK/page.html" -D "$h" -b "$1" -c "$1" "$url"
    url=$(header "$h" location)
    [[ "$url" == "$KC"/* ]] || break
  done
  kc_page "$WORK/page.html"
}

# aia_submit <jar> <поля curl…> — отправить форму страницы действия; печатает, куда сервис вернул браузер
aia_submit() {
  local jar=$1 h="$WORK/h" url
  shift
  curl -s -o "$WORK/page.html" -D "$h" -b "$jar" -c "$jar" "$@" "$(form_action "$WORK/page.html")"
  url=$(header "$h" location)
  [ -n "$url" ] || { echo "keycloak: $(kc_page "$WORK/page.html") $(feedback "$WORK/page.html")"; return; }
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$url"
  header "$h" location
}

# mail_link <адрес> — ссылка action-token из последнего письма на адрес (ждём до 10 секунд)
mail_link() {
  local link
  for _ in 1 2 3 4 5 6 7 8 9 10; do
    link=$(curl -s "$MAILPIT/api/v1/search?query=to:$1" | python3 -c '
import json,sys,re,html,urllib.request
messages = json.load(sys.stdin)["messages"]
if messages:
    body = json.load(urllib.request.urlopen(sys.argv[1] + "/api/v1/message/" + messages[0]["ID"]))
    links = [html.unescape(l) for l in re.findall(r"href=\"([^\"]+)\"", body["HTML"] or "") if "action-token" in l]
    print(links[0] if links else "")' "$MAILPIT")
    [ -n "$link" ] && { echo "$link"; return; }
    sleep 1
  done
}

# Письма прошлых прогонов на эти адреса — мешают найти новое.
curl -s -X DELETE "$MAILPIT/api/v1/search?query=to:$NEW_EMAIL" > /dev/null

# security <jar> <python-выражение над r> — раздел «Безопасность» кабинета (GET /me/security)
security() { curl -s -b "$1" "$API/me/security" | python3 -c "import json,sys; r=json.load(sys.stdin)['result']; print($2)"; }
# credential_id <тип> [телефон] — Id учётных данных пользователя проверки (Admin API)
credential_id() { admin_api "$KC/admin/realms/$REALM/users/$(user_id "${2:-$PHONE}")/credentials" | python3 -c 'import json,sys; print(next((c["id"] for c in json.load(sys.stdin) if c["type"] == sys.argv[1]), ""))' "$1"; }

profile() { curl -s -b "$1" "$API/me/profile" | python3 -c "import json,sys; r=json.load(sys.stdin)['result']; print($2)"; }

echo "0. Подготовка"
remove_user; remove_user "$OTHER_PHONE"
check "пользователи проверки заведены" "$(create_user "$PHONE" "$EMAIL") $(create_user "$OTHER_PHONE")" "201 201"
check "смена почты включена в realm, новая почта — только с подтверждением" \
  "$(admin_api "$KC/admin/realms/$REALM/authentication/required-actions/UPDATE_EMAIL" | python3 -c 'import json,sys; a=json.load(sys.stdin); print(a["enabled"], a["config"].get("verifyEmail"))')" "True true"

echo "1. Кабинет"
check "/auth/account ведёт в свой кабинет" "$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' "$API/auth/account")" "302 $API/account/"
check "кабинет отдаётся со строгой CSP" \
  "$(curl -s -o /dev/null -D - "$API/account/" | grep -ci "^content-security-policy: default-src 'self'; script-src 'self';")" 1
check "адрес раздела кабинета — та же страница (маршрут приложения)" \
  "$(curl -s -o "$WORK/page.html" -w '%{http_code}' "$API/account/security") $(grep -o '<title>[^<]*' "$WORK/page.html" | sed 's/<title>//')" \
  "200 Личный кабинет"
check "/auth/account/security — раздел «Безопасность» своего кабинета" \
  "$(curl -s -o /dev/null -w '%{redirect_url}' "$API/auth/account/security")" "$API/account/security"
check "без входа профиль — 401" "$(curl -s -o /dev/null -w '%{http_code}' "$API/me/profile")" 401
JAR="$WORK/jar"; : > "$JAR"
check "вход с возвратом в кабинет" "$(login "$JAR" /account/ "$PHONE")" /account/
check "профиль: телефон и почта с сервера входа" "$(profile "$JAR" 'r["phone"], r["email"], r["emailVerified"]')" "+$PHONE $EMAIL True"

echo "2. Смена пароля на странице Keycloak"
check "неизвестное действие — отказ" \
  "$(curl -s -o "$WORK/r.json" -w '%{http_code}' -b "$JAR" "$API/auth/login?return_to=/account/&action=delete_account") $(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["error"]["messages"][0]["code"])' "$WORK/r.json")" \
  "400 auth.action.unknown"
check "страница смены пароля — тема platform" "$(aia "$JAR" update-password) $(kc "$WORK/page.html" 'c["url"]["resourcesUrl"].split("/")[-1]')" "login-update-password.ftl platform"
check "отмена — возврат в кабинет с action_status=cancelled" \
  "$(aia_submit "$JAR" --data-urlencode "cancel-aia=true")" "/account/?action=update-password&action_status=cancelled"
aia "$JAR" update-password > /dev/null
check "новый пароль — возврат с action_status=success" \
  "$(aia_submit "$JAR" --data-urlencode "password-new=$NEW_PASSWORD" --data-urlencode "password-confirm=$NEW_PASSWORD")" \
  "/account/?action=update-password&action_status=success"
check "старый пароль больше не подходит" "$(login "$WORK/old" /account/ "$PHONE" "$PASSWORD" | cut -c1-9)" "keycloak:"
check "новый пароль подходит" "$(login "$WORK/new" /account/ "$PHONE" "$NEW_PASSWORD")" /account/

echo "3. Смена почты на странице Keycloak: новая — только после ссылки из письма"
check "страница смены почты" "$(aia "$JAR" update-email)" update-email.ftl
aia_submit "$JAR" --data-urlencode "email=$NEW_EMAIL" > /dev/null
check "письмо на новую почту, со страницы — назад в кабинет" \
  "$(kc_page "$WORK/page.html") $(feedback "$WORK/page.html" | grep -c "$NEW_EMAIL") $(kc "$WORK/page.html" 'get("client", "baseUrl")')" \
  "info.ftl 1 $API/auth/account"
check "до перехода по ссылке почта прежняя" "$(profile "$JAR" 'r["email"], r["emailVerified"]')" "$EMAIL True"
LINK=$(mail_link "$NEW_EMAIL")
curl -s -o "$WORK/page.html" -b "$JAR" -c "$JAR" "$LINK"
check "ссылка из письма — почта обновлена" "$(kc_page "$WORK/page.html") $(feedback "$WORK/page.html" | grep -c "$NEW_EMAIL")" "info.ftl 1"
check "профиль: новая почта, подтверждена" "$(profile "$JAR" 'r["email"], r["emailVerified"]')" "$NEW_EMAIL True"

echo "4. Безопасность: 2FA, коды восстановления, ключи доступа — на страницах Keycloak"
check "сначала ничего не подключено" "$(security "$JAR" 'r["hasPassword"], len(r["authenticators"]), len(r["passkeys"]), r["recoveryCodes"]')" "True 0 0 None"
check "страница подключения приложения-аутентификатора" "$(aia "$JAR" configure-totp)" login-config-totp.ftl
TOTP_SECRET=$(kc "$WORK/page.html" 'c["totp"]["totpSecret"]')
check "код из приложения — подключено, возврат с action_status=success" \
  "$(aia_submit "$JAR" --data-urlencode "totp=$(totp "$TOTP_SECRET")" --data-urlencode "totpSecret=$TOTP_SECRET" --data-urlencode "userLabel=Телефон")" \
  "/account/?action=configure-totp&action_status=success"
check "в кабинете — приложение «Телефон»" "$(security "$JAR" '[a["label"] for a in r["authenticators"]]')" "['Телефон']"
check "вход теперь спрашивает код после пароля" "$(login_until_done "$WORK/otp" /account/ "$PHONE" "$NEW_PASSWORD" > /dev/null; kc_page "$WORK/page.html")" login-otp.ftl
# Сессия началась без кода: с 2FA действия требуют войти заново — паролем и кодом (новым: старый Keycloak не примет).
check "с 2FA действие — сначала войти заново" "$(aia "$JAR" configure-recovery-codes)" login.ftl
curl -s -o "$WORK/page.html" -D "$WORK/h" -b "$JAR" -c "$JAR" --data-urlencode "username=$PHONE" --data-urlencode "password=$NEW_PASSWORD" "$(form_action "$WORK/page.html")"
check "после пароля — код из приложения" "$(kc_page "$WORK/page.html")" login-otp.ftl
curl -s -o "$WORK/page.html" -D "$WORK/h" -b "$JAR" -c "$JAR" --data-urlencode "otp=$(fresh_totp "$TOTP_SECRET")" "$(form_action "$WORK/page.html")"
URL=$(header "$WORK/h" location)
while [[ "$URL" == "$KC"/* ]]; do curl -s -o "$WORK/page.html" -D "$WORK/h" -b "$JAR" -c "$JAR" "$URL"; URL=$(header "$WORK/h" location); done
check "после кода — страница кодов восстановления" "$(kc_page "$WORK/page.html")" login-recovery-authn-code-config.ftl
check "коды сохранены — возврат с action_status=success" \
  "$(aia_submit "$JAR" --data-urlencode "generatedRecoveryAuthnCodes=$(kc "$WORK/page.html" 'c["recoveryAuthnCodesConfigBean"]["generatedRecoveryAuthnCodesAsString"]')" \
    --data-urlencode "generatedAt=$(kc "$WORK/page.html" 'c["recoveryAuthnCodesConfigBean"]["generatedAt"]')" --data-urlencode "userLabel=Коды")" \
  "/account/?action=configure-recovery-codes&action_status=success"
check "в кабинете — коды: осталось 12 из 12" "$(security "$JAR" 'r["recoveryCodes"]["remaining"], r["recoveryCodes"]["total"]')" "12 12"
check "удалить приложение — сначала подтверждение" "$(aia "$JAR" delete-credential "$(credential_id otp)")" delete-credential.ftl
check "подтвердил — удалено, возврат с action_status=success" \
  "$(aia_submit "$JAR" --data-urlencode "accept=")" "/account/?action=delete-credential&action_status=success"
check "в кабинете приложения больше нет" "$(security "$JAR" 'len(r["authenticators"])')" 0
check "без приложения коды остались вторым шагом: вход спрашивает код восстановления" \
  "$(login_until_done "$WORK/codes" /account/ "$PHONE" "$NEW_PASSWORD" > /dev/null; kc_page "$WORK/page.html")" login-recovery-authn-code-input.ftl
aia "$JAR" delete-credential "$(credential_id recovery-authn-codes)" > /dev/null
check "удалить и коды — 2FA выключена совсем" "$(aia_submit "$JAR" --data-urlencode "accept=")" "/account/?action=delete-credential&action_status=success"
check "вход снова без второго шага" "$(login "$WORK/plain" /account/ "$PHONE" "$NEW_PASSWORD")" /account/

check "ключ доступа — страница регистрации (сам ключ — только с устройством)" "$(aia "$JAR" register-passkey)" webauthn-register.ftl
check "пароль удалить нельзя: ссылкой в обход кабинета — отказ Keycloak" \
  "$(aia "$JAR" delete-credential "$(credential_id password)" > /dev/null; aia_submit "$JAR" --data-urlencode "accept=" | cut -c1-9) $(credential_id password | grep -c .)" \
  "keycloak: 1"
check "удалить — только с Id учётных данных" \
  "$(curl -s -o "$WORK/r.json" -w '%{http_code}' -b "$JAR" "$API/auth/login?return_to=/account/&action=delete-credential&credential=password") $(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["error"]["messages"][0]["code"])' "$WORK/r.json")" \
  "400 auth.action.credential_required"


echo "5. Чужие учётные данные из своей сессии не удалить"
OTHER="$WORK/other"; : > "$OTHER"
login "$OTHER" /account/ "$OTHER_PHONE" > /dev/null
aia "$OTHER" configure-totp > /dev/null
OTHER_SECRET=$(kc "$WORK/page.html" 'c["totp"]["totpSecret"]')
check "у второго пользователя — своё приложение" \
  "$(aia_submit "$OTHER" --data-urlencode "totp=$(totp "$OTHER_SECRET")" --data-urlencode "totpSecret=$OTHER_SECRET" --data-urlencode "userLabel=Чужой")" \
  "/account/?action=configure-totp&action_status=success"
OTHER_OTP=$(credential_id otp "$OTHER_PHONE")
# Keycloak не находит такие данные у пользователя сессии: без страницы подтверждения сразу возвращает с ошибкой.
aia "$JAR" delete-credential "$OTHER_OTP" > /dev/null
check "удалить его Id из своей сессии — без подтверждения, возврат с action_status=error" \
  "$(curl -s -o /dev/null -D "$WORK/h" -b "$JAR" -c "$JAR" "$(header "$WORK/h" location)"; header "$WORK/h" location)" \
  "/account/?action=delete-credential&action_status=error"
check "его приложение на месте" "$(credential_id otp "$OTHER_PHONE")" "$OTHER_OTP"

summary
