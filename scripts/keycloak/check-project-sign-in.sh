#!/usr/bin/env bash
# Проверка этапа 10.7 (docs/keycloak/10-code-settings.md, «Способы входа проекта»): админ проекта включает и выключает
# способы входа (телефон, почта, пароль, ключ доступа, Яндекс ID, Госуслуги), делает 2FA обязательной и оставляет
# переходный период. Выключенное не пускает и не регистрирует; в переходный период входят только «старые» люди,
# у которых другого способа нет, и им предлагают ключ доступа. Проверка заводит своих пользователей и в конце
# удаляет их, а способы входа возвращает как на стенде (всё включено, 2FA по желанию).
# Запуск: стенд поднят (Docker: сервис :8080, Keycloak :8180, заглушка телефонии :8190), затем
#   scripts/keycloak/check-project-sign-in.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM=sandbox
PLUGIN="$KC/realms/$REALM/platform-sign-in"
BFF_SECRET="${SANDBOX_BFF_CLIENT_SECRET:-$(grep -s '^SANDBOX_BFF_CLIENT_SECRET=' "$(dirname "$0")/../../.env" | cut -d= -f2)}"
OLD=79990000081           # только телефон: «старый» человек переходного периода
MIXED=79990000082         # телефон, почта и пароль
MIXED_EMAIL=project-sign-in-check@sandbox.test
NEW=79990000083           # только телефон, заведён уже в переходный период
UNKNOWN=79990000084       # такого нет: регистрация
UNTIL=$(TZ=Europe/Moscow date -d '+30 days' +%F)

# Запросы к Keycloak — «с нового IP»: прошлые проверки не упрутся в капчу (KC_PROXY_HEADERS стенда).
IP="10.$((RANDOM % 250)).$((RANDOM % 250)).$((RANDOM % 250 + 1))"
curl() { command curl -H "X-Forwarded-For: $IP" "$@"; }

admin_api() { command curl -s -H "Authorization: Bearer $(master_token)" -H 'Content-Type: application/json' "$@"; }
user_id() { admin_api "$KC/admin/realms/$REALM/users?username=$1&exact=true" | python3 -c 'import json,sys; u=json.load(sys.stdin); print(u[0]["id"] if u else "")'; }
remove_user() { local id; id=$(user_id "$1"); [ -z "$id" ] || admin_api -X DELETE "$KC/admin/realms/$REALM/users/$id" > /dev/null; }
# create_user <номер> [почта] — пользователь с телефоном; с почтой — ещё и пароль
create_user() {
  admin_api -o /dev/null -w '%{http_code}' -d "$(python3 -c 'import json,sys
user = {"username": sys.argv[1], "enabled": True, "attributes": {"phone": ["+" + sys.argv[1]]}}
if sys.argv[2]:
    user.update(email=sys.argv[2], emailVerified=True, credentials=[{"type": "password", "value": sys.argv[3], "temporary": False}])
print(json.dumps(user))' "$1" "${2:-}" "$PASSWORD")" "$KC/admin/realms/$REALM/users"
}
sa_token() {
  curl -s -d grant_type=client_credentials -d client_id=sandbox-bff -d client_secret="$BFF_SECRET" \
    "$KC/realms/$REALM/protocol/openid-connect/token" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])'
}
# body <способ=состояние>… — тело PUT: все способы включены, кроме названных; «phone=transition» — до UNTIL,
# «phone=transition:2020-01-01» — до даты; «2fa=required»
body() {
  python3 -c '
import json,sys
until = sys.argv[1]
methods = {m: {"status": "on"} for m in ["phone", "email", "password", "passkey", "yandex", "esia"]}
two = "optional"
for arg in sys.argv[2:]:
    key, value = arg.split("=", 1)
    if key == "2fa":
        two = value
    elif value.startswith("transition"):
        methods[key] = {"status": "transition", "until": value.split(":", 1)[1] if ":" in value else until}
    else:
        methods[key] = {"status": value}
print(json.dumps({"methods": methods, "twoFactor": two}))' "$UNTIL" "$@"
}
# api <jar> <метод> <путь> [тело] — печатает «<код> <код ошибки или ok>»
api() {
  curl -s -o "$WORK/r.json" -w '%{http_code}' -b "$1" -X "$2" -H 'Content-Type: application/json' ${4:+-d "$4"} "$API$3"
  python3 -c '
import json,sys
try: d=json.load(open(sys.argv[1]))
except Exception: print(" —"); sys.exit()
print(" " + (d["error"]["messages"][0]["code"] if d.get("error") else "ok"))' "$WORK/r.json"
}
# set_methods <способ=состояние>… — сохранить через сервис (как админка); печатает «<код> <ошибка или ok>»
set_methods() { api "$ADMIN" PUT /auth/admin/sign-in/methods "$(body "$@")"; }
# state — способы из сервиса: «phone:on email:on … 2fa:optional»
state() {
  curl -s -b "$ADMIN" "$API/auth/admin/sign-in/methods" | python3 -c '
import json,sys
r = json.load(sys.stdin)["result"]
print(" ".join(k + ":" + v["status"] + (":" + v["until"] if v.get("until") else "") for k, v in r["methods"].items()) + " 2fa:" + r["twoFactor"])'
}
restore() { command curl -s -o /dev/null -X PUT -H "Authorization: Bearer $(sa_token)" -H 'Content-Type: application/json' -d "$(body)" "$PLUGIN/methods"; }
# password_login <логин> — вход по логину и паролю; печатает, куда вернули, или «keycloak: <сообщение>»
password_login() { local jar="$WORK/try"; : > "$jar"; login "$jar" /me/ "$1"; }
# code_try <логин> — «Войти по коду»: экран кода или «ошибка: <сообщение>»
code_try() { local jar="$WORK/code"; : > "$jar"; code_start "$jar" "$1"; }
# offer_after_code <номер> — вход по коду до предложения ключа доступа; печатает platformSignInTransition («—» —
# предложения нет), затем «Не сейчас» и печатает, куда вернули
offer_after_code() {
  local jar="$WORK/code" h="$WORK/h" page="$WORK/page.html" url
  : > "$jar"
  code_start_fresh "$jar" "$1" > /dev/null
  curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" --data-urlencode "code=$(phone_code "$1")" "$(form_action "$page")"
  url=$(header "$h" location)
  if [[ "$url" == *execution=platform-passkey-offer* ]]; then
    curl -s -o "$page" -b "$jar" -c "$jar" "$url"
    printf '%s ' "$(kc "$page" 'c.get("platformSignInTransition") or "—"')"
    curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" --data-urlencode "skip=true" "$(form_action "$page")"
    url=$(header "$h" location)
  else
    printf '— '
  fi
  [ -n "$url" ] || { echo "keycloak: $(kc_page "$page") $(feedback "$page")"; return; }
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$url"
  header "$h" location
}
# after_password <логин> — вход по паролю, все переадресации; печатает страницу Keycloak, на которой остановились,
# или адрес сервиса
after_password() {
  local jar="$WORK/try" h="$WORK/h" page="$WORK/page.html" url
  : > "$jar"
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=/me/"
  curl -s -o "$page" -b "$jar" -c "$jar" "$(header "$h" location)"
  url=$(curl -s -L -o "$page" -w '%{url_effective}' -b "$jar" -c "$jar" --data-urlencode "username=$1" --data-urlencode "password=$PASSWORD" "$(form_action "$page")")
  if [ -n "$(kc_page "$page")" ]; then kc_page "$page"; else echo "${url#"$API"}"; fi
}
# providers — внешний вход на странице входа: «esia yandex»
providers() {
  local jar="$WORK/p" h="$WORK/h" page="$WORK/p.html"
  : > "$jar"
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=/me/"
  curl -s -o "$page" -b "$jar" -c "$jar" "$(header "$h" location)"
  kc "$page" '" ".join(sorted(p["alias"] for p in ((c.get("social") or {}).get("providers") or [])))'
}
realm_field() { admin_api "$KC/admin/realms/$REALM" | python3 -c "import json,sys; r=json.load(sys.stdin); print($1)"; }
idp_enabled() { admin_api "$KC/admin/realms/$REALM/identity-provider/instances/$1" | python3 -c 'import json,sys; print(json.load(sys.stdin)["enabled"])'; }
register_action() { admin_api "$KC/admin/realms/$REALM/authentication/required-actions/webauthn-register-passwordless" | python3 -c 'import json,sys; print(json.load(sys.stdin)["enabled"])'; }
last_audit() {
  curl -s -b "$ADMIN" "$API/auth/admin/audit?pageSize=1" | python3 -c '
import json,sys
i = json.load(sys.stdin)["result"]["items"][0]
c = json.loads(i["changes"]) if isinstance(i["changes"], str) else i["changes"]
print(i["action"], " ".join(k + ":" + v["new"] for k, v in sorted(c.items())))'
}

require_service
trap 'restore; for u in $OLD $MIXED $NEW $UNKNOWN; do remove_user $u; done; rm -rf "$WORK"' EXIT
restore
for u in $OLD $MIXED $NEW $UNKNOWN; do remove_user $u; done
ADMIN="$WORK/admin"; : > "$ADMIN"
MANAGER="$WORK/manager"; : > "$MANAGER"
login "$ADMIN" /admin/ 1234567890 > /dev/null
login "$MANAGER" /admin/ 0987654321 > /dev/null
check "пользователи проверки: только телефон; телефон, почта и пароль" "$(create_user $OLD) $(create_user $MIXED $MIXED_EMAIL)" "201 201"

echo "1. Способы входа в админке — только админу проекта"
check "без токена плагин не отвечает" "$(curl -s -o /dev/null -w '%{http_code}' "$PLUGIN/methods")" 401
check "менеджеру — нет" "$(api "$MANAGER" GET /auth/admin/sign-in/methods | cut -d' ' -f1)" 403
check "сейчас всё включено, 2FA — по желанию" "$(state)" \
  "phone:on email:on password:on passkey:on yandex:on esia:on 2fa:optional"
check "сколько людей входит по телефону и сколько — только по нему" \
  "$(curl -s -b "$ADMIN" "$API/auth/admin/sign-in/methods/phone/affected" | python3 -c 'import json,sys; r=json.load(sys.stdin)["result"]; print(r["method"], r["users"] >= 2, r["only"] >= 1, r["only"] < r["users"])')" \
  "phone True True True"
check "незнакомый способ" "$(api "$ADMIN" GET /auth/admin/sign-in/methods/fax/affected)" "404 sign_in.method_not_found"

echo "2. Неверное не сохраняется"
check "без способов регистрации" "$(set_methods phone=off email=off yandex=off esia=off)" "400 sign_in.no_registration"
check "переходный период ключа доступа" "$(set_methods passkey=transition)" "400 sign_in.transition_not_allowed"
check "переходный период до прошлой даты" "$(set_methods phone=transition:2020-01-01)" "400 sign_in.transition_date"
check "плагин тоже не принимает (мимо сервиса)" \
  "$(command curl -s -X PUT -H "Authorization: Bearer $(sa_token)" -H 'Content-Type: application/json' -d "$(body phone=off email=off yandex=off esia=off)" "$PLUGIN/methods" | python3 -c 'import json,sys; print("error" in json.load(sys.stdin))')" True
check "ничего не поменялось" "$(state)" "phone:on email:on password:on passkey:on yandex:on esia:on 2fa:optional"

echo "3. Пароль выключен"
check "сохранено" "$(set_methods password=off)" "200 ok"
check "в журнале — что поменялось" "$(last_audit)" "ProjectSignInChanged password:off"
check "верный пароль не пускает — понятное сообщение" "$(password_login $MIXED)" \
  "keycloak: Вход по паролю в проекте выключен. Войдите по коду или ключом доступа."
check "по коду — входит" "$(code_try $MIXED_EMAIL)" "Отправили код письмом на $MIXED_EMAIL."

echo "4. Телефон выключен"
check "сохранено" "$(set_methods phone=off)" "200 ok"
check "по номеру с паролем — нет, подсказка про почту" "$(password_login $MIXED)" \
  "keycloak: Вход по номеру телефона в проекте выключен. Войдите по почте."
check "по почте с паролем — входит" "$(password_login $MIXED_EMAIL)" /me/
CALLS=$(phone_count $OLD)
check "код на номер — нет" "$(code_try $OLD)" "ошибка: Вход по номеру телефона в проекте выключен. Войдите по почте."
check "звонка и SMS не было" "$(phone_count $OLD)" "$CALLS"
check "незнакомый номер — не зарегистрировать" "$(code_try $UNKNOWN) $(user_id $UNKNOWN)" \
  "ошибка: Вход по номеру телефона в проекте выключен. Войдите по почте. "

echo "5. Переходный период по телефону (до $UNTIL)"
check "сохранено" "$(set_methods phone=transition)" "200 ok"
check "в админке — до какого числа" "$(state | cut -d' ' -f1)" "phone:transition:$UNTIL"
check "в журнале — до какого числа" "$(last_audit)" "ProjectSignInChanged phone:transition до $UNTIL"
check "заведён после начала периода" "$(create_user $NEW)" 201
check "«старый» человек только с телефоном входит, ему предлагают ключ доступа" "$(offer_after_code $OLD)" "phone:$UNTIL /me/"
check "и при следующем входе — снова" "$(offer_after_code $OLD)" "phone:$UNTIL /me/"
check "у кого есть почта — по номеру уже нет" "$(password_login $MIXED)" \
  "keycloak: Вход по номеру телефона в проекте выключен. Войдите по почте."
check "заведённый после начала — по номеру нет" "$(code_try $NEW)" \
  "ошибка: Вход по номеру телефона в проекте выключен. Войдите по почте."
check "незнакомый номер — не зарегистрировать" "$(code_try $UNKNOWN) $(user_id $UNKNOWN)" \
  "ошибка: Вход по номеру телефона в проекте выключен. Войдите по почте. "

echo "6. 2FA обязательна"
check "сохранено" "$(set_methods 2fa=required)" "200 ok"
check "в журнале" "$(last_audit)" "ProjectSignInChanged phone:on twoFactor:required"
check "без приложения-аутентификатора — сразу завести его" "$(after_password $MIXED)" login-config-totp.ftl

echo "7. Ключ доступа, Яндекс ID и Госуслуги выключаются в самом Keycloak"
check "на странице входа есть Яндекс ID и Госуслуги" "$(providers)" "esia yandex"
check "сохранено" "$(set_methods passkey=off yandex=off)" "200 ok"
check "ключи доступа realm и регистрация ключа — выключены" \
  "$(realm_field 'r["webAuthnPolicyPasswordlessPasskeysEnabled"]') $(register_action)" "False False"
check "Яндекс ID выключен, Госуслуги — нет" "$(idp_enabled yandex) $(idp_enabled esia)" "False True"
check "на странице входа — только Госуслуги" "$(providers)" esia
check "в админке Яндекс ID — выключен, но настроен" \
  "$(curl -s -b "$ADMIN" "$API/auth/admin/sign-in/methods" | python3 -c 'import json,sys; y=json.load(sys.stdin)["result"]["methods"]["yandex"]; print(y["status"], y["configured"])')" "off True"
check "всё обратно" "$(set_methods)" "200 ok"
check "ключи доступа, регистрация ключа и Яндекс ID — снова включены" \
  "$(realm_field 'r["webAuthnPolicyPasswordlessPasskeysEnabled"]') $(register_action) $(idp_enabled yandex)" "True True True"
check "в админке — всё включено" "$(state)" "phone:on email:on password:on passkey:on yandex:on esia:on 2fa:optional"
check "по номеру с паролем — снова входит" "$(password_login $MIXED)" /me/

summary
