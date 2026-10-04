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
EMAIL=account-check@sandbox.test
NEW_EMAIL=account-check-new@sandbox.test
NEW_PASSWORD='Changed1234!'

MASTER=$(master_token)
admin_api() { curl -s -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' "$@"; }
user_id() { admin_api "$KC/admin/realms/$REALM/users?username=$PHONE&exact=true" | python3 -c 'import json,sys; u=json.load(sys.stdin); print(u[0]["id"] if u else "")'; }
remove_user() { local id; id=$(user_id); [ -z "$id" ] || admin_api -X DELETE "$KC/admin/realms/$REALM/users/$id" > /dev/null; }
trap 'remove_user; rm -rf "$WORK"' EXIT

# aia <jar> <действие> — вход с действием из кабинета; страница действия Keycloak — в $WORK/page.html
# (Keycloak сначала переадресует на login-actions/required-action — идём по его переадресациям до страницы).
aia() {
  local h="$WORK/h" url
  curl -s -o /dev/null -D "$h" -b "$1" -c "$1" "$API/auth/login?return_to=/account/?action=$2&action=$2"
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

profile() { curl -s -b "$1" "$API/me/profile" | python3 -c "import json,sys; r=json.load(sys.stdin)['result']; print($2)"; }

echo "0. Подготовка"
remove_user
# JSON — заранее, не внутри "$(…)": bash 3.2 (macOS) раскрывает там фигурные скобки и рвёт тело на части.
USER_JSON=$(python3 -c 'import json,sys; print(json.dumps({"username": sys.argv[1], "enabled": True, "email": sys.argv[2], "emailVerified": True,
  "attributes": {"phone": ["+" + sys.argv[1]]}, "credentials": [{"type": "password", "value": sys.argv[3], "temporary": False}]}))' "$PHONE" "$EMAIL" "$PASSWORD")
check "пользователь проверки заведён" "$(admin_api -o /dev/null -w '%{http_code}' -d "$USER_JSON" "$KC/admin/realms/$REALM/users")" 201
check "смена почты включена в realm, новая почта — только с подтверждением" \
  "$(admin_api "$KC/admin/realms/$REALM/authentication/required-actions/UPDATE_EMAIL" | python3 -c 'import json,sys; a=json.load(sys.stdin); print(a["enabled"], a["config"].get("verifyEmail"))')" "True true"

echo "1. Кабинет"
check "/auth/account ведёт в свой кабинет" "$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' "$API/auth/account")" "302 $API/account/"
check "кабинет отдаётся со строгой CSP" \
  "$(curl -s -o /dev/null -D - "$API/account/" | grep -ci "^content-security-policy: default-src 'self'; script-src 'self';")" 1
check "адрес раздела кабинета — та же страница (маршрут приложения)" \
  "$(curl -s -o "$WORK/page.html" -w '%{http_code}' "$API/account/security") $(grep -o '<title>[^<]*' "$WORK/page.html" | sed 's/<title>//')" \
  "200 Личный кабинет"
check "«Безопасность» (2FA, ключи доступа) — пока раздел «Вход» кабинета Keycloak" \
  "$(curl -s -o /dev/null -w '%{redirect_url}' "$API/auth/account/security")" "$KC/realms/$REALM/account/account-security/signing-in"
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

summary
