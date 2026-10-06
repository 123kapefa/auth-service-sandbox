#!/usr/bin/env bash
# Проверка этапа 10.3 (docs/keycloak/10-sign-in-methods.md): человек выключает способы входа — пароль, код на телефон,
# код на почту; выключенный не пускает даже с верным паролем, «Забыли пароль» письмо не шлёт; правила не дают запереть
# себя; уведомление письмом; админ возвращает способы по умолчанию. Проверка заводит своего пользователя (с ключом
# доступа — без него пароль не выключить) и удаляет его в конце.
# Запуск: стенд поднят (Docker: сервис :8080, Keycloak :8180, Mailpit :8025, заглушка телефонии :8190), затем
#   scripts/keycloak/check-sign-in-methods.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
MAILPIT="${MAILPIT:-http://localhost:${MAILPIT_UI_PORT:-8025}}"
REALM=sandbox
PHONE=79990000096
EMAIL=sign-in-check@sandbox.test
ADMIN_LOGIN=1234567890 # админ в режиме admin (71234567890)

MASTER=$(master_token)
admin_api() { curl -s -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' "$@"; }
user_id() { admin_api "$KC/admin/realms/$REALM/users?username=$PHONE&exact=true" | python3 -c 'import json,sys; u=json.load(sys.stdin); print(u[0]["id"] if u else "")'; }
remove_user() { local id; id=$(user_id); [ -z "$id" ] || admin_api -X DELETE "$KC/admin/realms/$REALM/users/$id" > /dev/null; }
# attribute — значение атрибута disabled_sign_in_methods в Keycloak («—» — нет)
attribute() { admin_api "$KC/admin/realms/$REALM/users/$USER_ID" | python3 -c 'import json,sys; print((json.load(sys.stdin).get("attributes") or {}).get("disabled_sign_in_methods", ["—"])[0])'; }
# unlock — снять временную блокировку от подбора пароля (неудачные входы подряд её включают)
unlock() { admin_api -X DELETE "$KC/admin/realms/$REALM/attack-detection/brute-force/users/$USER_ID" > /dev/null; }
# passkey_id — Id ключа доступа пользователя (пусто — нет)
passkey_id() { admin_api "$KC/admin/realms/$REALM/users/$USER_ID/credentials" | python3 -c 'import json,sys; print(next((c["id"] for c in json.load(sys.stdin) if c["type"] == "webauthn-passwordless"), ""))'; }

# Пользователь проверки: пароль, телефон, почта и ключ доступа. Ключ — запись без настоящего устройства: для правил
# важно, что он есть, входить им проверка не будет.
create_user() {
  local json
  json=$(python3 -c 'import json,sys
cred = {"aaguid": "00000000-0000-0000-0000-000000000000", "credentialId": "Y2hlY2s", "counter": 0,
        "credentialPublicKey": "pQECAyYgASFYIA", "attestationStatementFormat": "none", "transports": []}
print(json.dumps({"username": sys.argv[1], "enabled": True, "email": sys.argv[2], "emailVerified": True,
  "attributes": {"phone": ["+" + sys.argv[1]]},
  "credentials": [{"type": "password", "value": sys.argv[3], "temporary": False},
                  {"type": "webauthn-passwordless", "userLabel": "Ключ проверки", "secretData": "{}", "credentialData": json.dumps(cred)}]}))' \
    "$PHONE" "$EMAIL" "$PASSWORD")
  admin_api -o /dev/null -w '%{http_code}' -d "$json" "$KC/admin/realms/$REALM/users"
}
trap 'remove_user; rm -rf "$WORK"' EXIT

# methods <jar> — способы входа из кабинета: «password:вкл phone-code:вкл email-code:выкл»
methods() {
  curl -s -b "$1" "$API/me/sign-in-methods" | python3 -c '
import json,sys
r = json.load(sys.stdin)["result"]
print(" ".join(m["method"] + (":вкл" if m["enabled"] else ":выкл") for m in r["methods"]))'
}
# blocker <jar> <способ> — почему способ нельзя выключить (пусто — можно)
blocker() {
  curl -s -b "$1" "$API/me/sign-in-methods" | python3 -c '
import json,sys
print(next(m["disableBlocker"] or "" for m in json.load(sys.stdin)["result"]["methods"] if m["method"] == sys.argv[1]))' "$2"
}
# set_method <jar> <способ> <true|false> [выйти на других: true|false] — PUT; печатает «<HTTP-код> <код ошибки или ok>»
set_method() {
  curl -s -o "$WORK/set.json" -w '%{http_code}' -b "$1" -c "$1" -X PUT -H 'Content-Type: application/json' \
    -d "{\"enabled\":$3,\"signOutOtherDevices\":${4:-true}}" "$API/me/sign-in-methods/$2"
  python3 -c '
import json,sys
d = json.load(open(sys.argv[1]))
print(" " + ("ok" if d.get("result") else d["error"]["messages"][0]["code"]))' "$WORK/set.json"
}
# history <jar> — изменения из кабинета, новые сверху: «email-code:выкл:сам password:выкл:сам» («сброс:админ» — админ
# вернул способы по умолчанию)
history() {
  curl -s -b "$1" "$API/me/sign-in-methods" | python3 -c '
import json,sys
r = json.load(sys.stdin)["result"]
print(" ".join((h["method"] or "сброс") + ("" if h["enabled"] is None else (":вкл" if h["enabled"] else ":выкл"))
               + (":админ" if h["byAdmin"] else ":сам") for h in r["history"]))'
}
# mails <тема> — сколько писем с такой темой пришло пользователю
mails() {
  curl -s "$MAILPIT/api/v1/search?query=to:$EMAIL" | python3 -c '
import json,sys
print(sum(1 for m in json.load(sys.stdin)["messages"] if m["Subject"] == sys.argv[1]))' "$1"
}
# last_notice — текст последнего уведомления о способах входа (ждём до 10 секунд, пока придёт новое)
last_notice() {
  local i text
  for i in 1 2 3 4 5 6 7 8 9 10; do
    text=$(curl -s "$MAILPIT/api/v1/search?query=to:$EMAIL" | python3 -c '
import json,sys,urllib.request
messages = [m for m in json.load(sys.stdin)["messages"] if m["Subject"] == "Способы входа изменены"]
if len(messages) > int(sys.argv[2]):
    body = json.load(urllib.request.urlopen(sys.argv[1] + "/api/v1/message/" + messages[0]["ID"]))
    print(body["Text"].strip().splitlines()[0])' "$MAILPIT" "$1")
    [ -n "$text" ] && { echo "$text"; return; }
    sleep 1
  done
  echo "письма нет"
}
# reset_request <jar> <логин> — «Забыли пароль?» со страницы входа; печатает сообщение Keycloak (как в check-login-methods.sh)
reset_request() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html" url
  : > "$jar"
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=/me/"
  curl -s -o "$page" -b "$jar" -c "$jar" "$(header "$h" location)"
  url=$(kc "$page" 'c["url"]["loginResetCredentialsUrl"]')
  curl -s -o "$page" -b "$jar" -c "$jar" "$KC$url"
  [ "$(kc_page "$page")" = login-reset-password.ftl ] || { echo "нет формы сброса"; return; }
  curl -s -o "$page" -b "$jar" -c "$jar" --data-urlencode "username=$2" "$(form_action "$page")"
  feedback "$page"
}
# direct_grant — токен по логину и паролю (клиент отладки dev-cli): «токен» или ошибка
direct_grant() {
  curl -s -d grant_type=password -d client_id=dev-cli -d "username=$PHONE" --data-urlencode "password=$PASSWORD" \
    "$KC/realms/$REALM/protocol/openid-connect/token" | python3 -c '
import json,sys
d = json.load(sys.stdin)
print("токен" if "access_token" in d else d.get("error", "") + ": " + d.get("error_description", ""))'
}
# me_status <jar> — HTTP-код GET /me/ (сессия жива — 200)
me_status() { curl -s -o /dev/null -w '%{http_code}' -b "$1" "$API/me/"; }

require_service
WORK=$(mktemp -d)
remove_user
curl -s -X DELETE "$MAILPIT/api/v1/search?query=to:$EMAIL" > /dev/null
check "пользователь проверки создан (пароль, телефон, почта, ключ доступа)" "$(create_user)" 201
USER_ID=$(user_id)

echo "1. Способы входа в кабинете (GET /me/sign-in-methods)"
JAR="$WORK/jar"; : > "$JAR"
OTHER="$WORK/other"; : > "$OTHER"
check "вход по паролю" "$(login "$JAR" /me/ "$PHONE")" /me/
check "вход на другом устройстве" "$(login "$OTHER" /me/ "$PHONE")" /me/
check "все три способа включены" "$(methods "$JAR")" "password:вкл phone-code:вкл email-code:вкл"
check "с ключом доступа пароль можно выключить" "$(blocker "$JAR" password)" ""
check "токен по паролю (dev-cli) выдаётся" "$(direct_grant)" "токен"

echo "2. Выключить пароль (PUT /me/sign-in-methods/password)"
check "выключен" "$(set_method "$JAR" password false)" "200 ok"
check "в кабинете — выключен" "$(methods "$JAR")" "password:выкл phone-code:вкл email-code:вкл"
check "в Keycloak — атрибут disabled_sign_in_methods" "$(attribute)" password
check "письмо человеку" "$(last_notice 0)" "В Auth Sandbox выключен вход по паролю: так войти в ваш аккаунт больше нельзя."
check "другое устройство вышло" "$(me_status "$OTHER")" 401
check "это устройство — в кабинете" "$(me_status "$JAR")" 200
check "в истории — изменение" "$(history "$JAR")" "password:выкл:сам"
check "повтор — без нового письма" "$(set_method "$JAR" password false) $(mails 'Способы входа изменены')" "200 ok 1"

echo "3. Выключенный пароль не пускает"
TRY="$WORK/try"; : > "$TRY"
WRONG=$(login "$TRY" /me/ "$PHONE" 'Wrong-1234!')
sleep 2; unlock; : > "$TRY"
check "верный пароль — тот же ответ, что на неверный" "$(login "$TRY" /me/ "$PHONE")" "$WRONG"
sleep 2; unlock
check "токен по паролю (dev-cli) — нет" "$(direct_grant)" "invalid_grant: Invalid user credentials"
unlock

echo "4. «Забыли пароль» при выключенном пароле"
RESET_BEFORE=$(mails 'Сброс пароля')
check "страница — как после отправки письма" "$(reset_request "$WORK/reset" "$PHONE")" \
  "В ближайшее время Вы должны получить письмо с дальнейшими инструкциями."
sleep 3
check "письма со ссылкой нет" "$(mails 'Сброс пароля')" "$RESET_BEFORE"

echo "5. Код на почту выключен"
check "выключен (остаться на других устройствах)" "$(set_method "$JAR" email-code false false)" "200 ok"
last_notice 1 > /dev/null
CODE_MAILS=$(mails 'Код для входа')
CODE="$WORK/code"; : > "$CODE"
check "экран кода — как обычно" "$(code_start "$CODE" "$EMAIL")" "Отправили код письмом на $EMAIL."
sleep 3
check "письма с кодом нет" "$(mails 'Код для входа')" "$CODE_MAILS"
check "любой код не подходит" "$(starts "$(code_submit "$CODE" 000000)" "keycloak:")" "keycloak:"

echo "6. Код на телефон выключен — выключено всё, остался ключ доступа"
CALLS=$(phone_count "$PHONE")
check "выключен" "$(set_method "$JAR" phone-code false false)" "200 ok"
check "в кабинете — всё выключено" "$(methods "$JAR")" "password:выкл phone-code:выкл email-code:выкл"
: > "$CODE"
check "экран звонка — как обычно" "$(starts "$(code_start "$CODE" "$PHONE")" "Вам звонят на ")" "Вам звонят на "
check "звонка и SMS нет" "$(phone_count "$PHONE")" "$CALLS"

echo "7. Админ: способы входа пользователя и «вернуть по умолчанию»"
ADMIN="$WORK/admin"; : > "$ADMIN"
login "$ADMIN" /me/ "$ADMIN_LOGIN" > /dev/null
check "админ видит способы входа" "$(curl -s -b "$ADMIN" "$API/auth/admin/users/$USER_ID/sign-in-methods" | python3 -c 'import json,sys; print(sum(1 for m in json.load(sys.stdin)["result"]["methods"] if not m["enabled"]))')" 3
check "ссылку на новый пароль не шлёт — пароль выключен" \
  "$(curl -s -b "$ADMIN" -X POST "$API/auth/admin/users/$USER_ID/password-reset" | python3 -c 'import json,sys; print(json.load(sys.stdin)["error"]["messages"][0]["code"])')" users.password_disabled
NOTICES=$(mails 'Способы входа изменены')
check "вернуть по умолчанию" "$(curl -s -o /dev/null -w '%{http_code}' -b "$ADMIN" -X POST "$API/auth/admin/users/$USER_ID/sign-in-methods/reset")" 200
check "в кабинете — всё включено" "$(methods "$JAR")" "password:вкл phone-code:вкл email-code:вкл"
check "атрибута больше нет" "$(attribute)" "—"
check "письмо человеку" "$(last_notice "$NOTICES")" \
  "Администратор Auth Sandbox вернул способы входа по умолчанию: снова работают пароль и коды на телефон и почту."
check "в истории — админ" "$(history "$JAR" | cut -d' ' -f1)" "сброс:админ"
: > "$TRY"
check "пароль снова пускает" "$(login "$TRY" /me/ "$PHONE")" /me/
reset_request "$WORK/reset" "$PHONE" > /dev/null
sleep 3
check "«Забыли пароль» снова шлёт письмо" "$(mails 'Сброс пароля')" "$((RESET_BEFORE + 1))"

echo "8. Нельзя запереть себя: без ключа доступа пароль снова работает"
check "пароль выключен" "$(set_method "$JAR" password false false)" "200 ok"
admin_api -X DELETE "$KC/admin/realms/$REALM/users/$USER_ID/credentials/$(passkey_id)" > /dev/null
check "ключ доступа удалён (как на странице Keycloak)" "$(passkey_id)" ""
check "в кабинете пароль снова включён" "$(methods "$JAR")" "password:вкл phone-code:вкл email-code:вкл"
: > "$TRY"
check "и Keycloak по нему пускает" "$(login "$TRY" /me/ "$PHONE")" /me/
check "выключить пароль без ключа и аутентификатора — нельзя" "$(set_method "$JAR" password false)" "409 sign_in_methods.needs_passkey_or_authenticator"
check "код на телефон — тоже" "$(set_method "$JAR" phone-code false)" "409 sign_in_methods.needs_passkey_or_authenticator"
check "неизвестный способ — 404" "$(set_method "$JAR" passkey false)" "404 sign_in_methods.unknown_method"
check "без входа — 401" "$(curl -s -o /dev/null -w '%{http_code}' "$API/me/sign-in-methods")" 401

summary
