#!/usr/bin/env bash
# Проверка этапа 4 (docs/keycloak/04-admin.md): режимы и администрирование через API сервиса.
# Запуск: сервис и Keycloak подняты, затем
#   scripts/keycloak/check-admin.sh                            # Docker: http://localhost:8080
#   API=http://localhost:5080 scripts/keycloak/check-admin.sh    # Development: dotnet run
# Скрипт возвращает пользователей в исходный режим — на него опираются остальные проверки.
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

# mode <jar> <режим> — POST /auth/mode; печатает «<HTTP-код> <activeMode или код ошибки>»
mode() {
  curl -s -o "$WORK/mode.json" -w '%{http_code}' -b "$1" -c "$1" -H 'Content-Type: application/json' \
    -d "{\"activeMode\":\"$2\"}" "$API/auth/mode"
  printf ' '
  python3 -c '
import json,sys
try: d=json.load(open(sys.argv[1]))
except Exception: print("—"); sys.exit()
print(d["result"]["activeMode"] if d.get("result") else d["error"]["messages"][0]["code"])' "$WORK/mode.json"
}

# me <jar> <поле> — поле из GET /me/ (списки — через запятую)
# admin_get <jar> <путь> <python-выражение над result> — GET к API админки; печатает «<код> <значение>»
admin_get() {
  curl -s -o "$WORK/admin.json" -w '%{http_code}' -b "$1" "$API$2"
  printf ' '
  python3 -c '
import json,sys
try: d=json.load(open(sys.argv[1]))
except Exception: print("—"); sys.exit()
r=d.get("result"); print(eval(sys.argv[2]) if r is not None else d["error"]["messages"][0]["code"])' "$WORK/admin.json" "$3"
}

# restrict <jar> <id> <true|false> [причина] — PUT …/restriction; печатает «<код> <isRestricted или код ошибки>»
restrict() {
  local body
  body=$(python3 -c 'import json,sys; d={"restricted": sys.argv[1]=="true"}; d.update({"reason": sys.argv[2]} if len(sys.argv)>2 else {}); print(json.dumps(d))' "${@:3}")
  curl -s -o "$WORK/admin.json" -w '%{http_code}' -b "$1" -X PUT -H 'Content-Type: application/json' -d "$body" \
    "$API/auth/admin/users/$2/restriction"
  printf ' '
  python3 -c '
import json,sys
d=json.load(open(sys.argv[1])); r=d.get("result")
print(r["isRestricted"] if r is not None else d["error"]["messages"][0]["code"])' "$WORK/admin.json"
}

# role <jar> <id> <роль> — PUT …/role; печатает «<код> <role или код ошибки>»
role() {
  curl -s -o "$WORK/admin.json" -w '%{http_code}' -b "$1" -X PUT -H 'Content-Type: application/json' \
    -d "{\"role\":\"$3\"}" "$API/auth/admin/users/$2/role"
  printf ' '
  python3 -c '
import json,sys
d=json.load(open(sys.argv[1])); r=d.get("result")
print(r["role"] if r is not None else d["error"]["messages"][0]["code"])' "$WORK/admin.json"
}

# app_access <jar> <id> <clientId> <true|false> — PUT …/apps/{clientId}/access; печатает «<код> <доступ к приложению или код ошибки>»
app_access() {
  curl -s -o "$WORK/admin.json" -w '%{http_code}' -b "$1" -X PUT -H 'Content-Type: application/json' \
    -d "{\"hasAccess\":$4}" "$API/auth/admin/users/$2/apps/$3/access"
  printf ' '
  python3 -c '
import json,sys
d=json.load(open(sys.argv[1])); r=d.get("result")
print([a["hasAccess"] for a in r if a["clientId"]==sys.argv[2]][0] if r is not None else d["error"]["messages"][0]["code"])' "$WORK/admin.json" "$3"
}

# my_sessions <jar> <python-выражение над result> — GET /security/sessions
my_sessions() { admin_get "$1" /security/sessions "$2" | cut -d' ' -f2-; }

# post <jar> <путь> — POST; печатает «<код> <тело result или код ошибки>»
post() {
  curl -s -o "$WORK/admin.json" -w '%{http_code}' -b "$1" -X POST "$API$2"
  printf ' '
  python3 -c '
import json,sys
d=json.load(open(sys.argv[1])); r=d.get("result")
print(eval(sys.argv[2]) if r is not None else d["error"]["messages"][0]["code"])' "$WORK/admin.json" "$3"
}

me() { curl -s -b "$1" "$API/me/" | python3 -c 'import json,sys; v=json.load(sys.stdin)["result"][sys.argv[1]]; print(",".join(v) if isinstance(v,list) else v)' "$2" 2>/dev/null; }

require_service

# На случай прошлого упавшего прогона: 2FA покупателя из раздела 9 (или невыполненное «настроить 2FA») мешала бы
# всем его входам — убираем мастер-админкой до начала.
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
MASTER=$(master_token)
CUSTOMER_KC_ID=$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/sandbox/users?username=79990000001&exact=true" |
  python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])')
curl -s -o /dev/null -X PUT -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' -d '{"requiredActions":[]}' \
  "$KC/admin/realms/sandbox/users/$CUSTOMER_KC_ID"
for CREDENTIAL in $(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/sandbox/users/$CUSTOMER_KC_ID/credentials" |
  python3 -c 'import json,sys; print(" ".join(c["id"] for c in json.load(sys.stdin) if c["type"] in ("otp", "recovery-authn-codes")))'); do
  curl -s -o /dev/null -X DELETE -H "Authorization: Bearer $MASTER" "$KC/admin/realms/sandbox/users/$CUSTOMER_KC_ID/credentials/$CREDENTIAL"
done

echo "1. Переключение режима (POST /auth/mode)"
JAR="$WORK/jar"; : > "$JAR"
check "вход админа 79990000002 (режим customer)" "$(login "$JAR" /me/ 79990000002)" /me/
check "сейчас режим customer" "$(me "$JAR" activeMode)" customer
check "в режиме customer прав админа нет" "$(status "$JAR" GET /demo/admin-only/)" 403
check "переход в admin" "$(mode "$JAR" admin)" "200 admin"
check "новый токен уже в cookie: /me/ — режим admin" "$(me "$JAR" activeMode)" admin
check "права админа появились" "$(status "$JAR" GET /demo/admin-only/)" 200
check "режим сохранён в Keycloak: после refresh — admin" "$(status "$JAR" POST /auth/refresh > /dev/null; me "$JAR" activeMode)" admin
check "режим manager недоступен — 403" "$(mode "$JAR" manager)" "403 auth.mode.not_available"
check "неизвестный режим — 403" "$(mode "$JAR" courier)" "403 auth.mode.not_available"
check "пустой режим — 400" "$(mode "$JAR" '')" "400 value.is.required"
check "запись режима не затёрла телефон" "$(me "$JAR" phone)" +79990000002
check "обратно в customer" "$(mode "$JAR" customer)" "200 customer"
check "прав админа снова нет" "$(status "$JAR" GET /demo/admin-only/)" 403
status "$JAR" POST /auth/logout > /dev/null

JAR="$WORK/jar2"; : > "$JAR"
login "$JAR" /me/ 79990000001 > /dev/null
check "покупатель в admin — 403" "$(mode "$JAR" admin)" "403 auth.mode.not_available"
check "покупатель в customer — можно" "$(mode "$JAR" customer)" "200 customer"
status "$JAR" POST /auth/logout > /dev/null
check "без входа — 401" "$(curl -s -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -d '{"activeMode":"admin"}' "$API/auth/mode")" 401

echo "2. Пользователи: список и карточка (право users.view)"
ADMIN="$WORK/admin"; : > "$ADMIN"
login "$ADMIN" /me/ 1234567890 > /dev/null   # админ в режиме admin; номер «по-старому» — 71234567890
MANAGER="$WORK/manager"; : > "$MANAGER"
login "$MANAGER" /me/ 0987654321 > /dev/null # менеджер в режиме manager
CUSTOMER="$WORK/customer"; : > "$CUSTOMER"
login "$CUSTOMER" /me/ 79990000001 > /dev/null
USERS='",".join(sorted(i["username"] for i in r["items"]))'
check "админ видит сотрудников" "$(admin_get "$ADMIN" '/auth/admin/users?search=7123' "$USERS")" "200 71234567890"
check "поиск по номеру «как набрали»" "$(admin_get "$ADMIN" '/auth/admin/users?search=%2B7%20(999)%20000-00-01' "$USERS")" "200 79990000001"
check "поиск по почте" "$(admin_get "$ADMIN" '/auth/admin/users?search=reset@sandbox' "$USERS")" "200 79990000005"
check "роль в списке" "$(admin_get "$ADMIN" '/auth/admin/users?search=70987654321' 'r["items"][0]["role"]')" "200 manager"
check "блокировка видна в списке" "$(admin_get "$ADMIN" '/auth/admin/users?search=79990000004' 'r["items"][0]["isRestricted"]')" "200 True"
check "страницы: pageSize=2 → 2 строки и есть ещё" "$(admin_get "$ADMIN" '/auth/admin/users?pageSize=2' 'str(len(r["items"])) + " " + str(r["hasMore"])')" "200 2 True"
check "менеджер не видит сотрудников" "$(admin_get "$MANAGER" '/auth/admin/users?search=7123' "$USERS")" "200 "
check "менеджер видит покупателей" "$(admin_get "$MANAGER" '/auth/admin/users?search=79990000001' "$USERS")" "200 79990000001"
ID_CUSTOMER=$(curl -s -b "$ADMIN" "$API/auth/admin/users?search=79990000001" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["items"][0]["id"])')
ID_ADMIN=$(curl -s -b "$ADMIN" "$API/auth/admin/users?search=71234567890" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["items"][0]["id"])')
check "карточка: роль, режимы, пароль" "$(admin_get "$ADMIN" "/auth/admin/users/$ID_ADMIN" 'r["role"] + " " + ",".join(r["availableRoles"]) + " " + str(r["hasPassword"])')" "200 admin customer,admin True"
check "менеджер: карточка покупателя" "$(admin_get "$MANAGER" "/auth/admin/users/$ID_CUSTOMER" 'r["phone"]')" "200 +79990000001"
check "менеджер: карточка сотрудника — 403" "$(admin_get "$MANAGER" "/auth/admin/users/$ID_ADMIN" 'r')" "403 users.staff_forbidden"
check "нет такого пользователя — 404" "$(admin_get "$ADMIN" "/auth/admin/users/00000000-0000-0000-0000-000000000000" 'r')" "404 users.not_found"
check "покупателю список недоступен — 403" "$(status "$CUSTOMER" GET /auth/admin/users)" 403
check "неверный размер страницы — 400" "$(status "$ADMIN" GET '/auth/admin/users?pageSize=500')" 400
status "$CUSTOMER" POST /auth/logout > /dev/null

echo "3. Блокировка аккаунта (право users.block)"
ID_MANAGER=$(curl -s -b "$ADMIN" "$API/auth/admin/users?search=70987654321" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["items"][0]["id"])')
restrict "$ADMIN" "$ID_CUSTOMER" false > /dev/null   # на случай прошлого упавшего прогона
CUSTOMER="$WORK/customer2"; : > "$CUSTOMER"
login "$CUSTOMER" /me/ 79990000001 > /dev/null
OLD_ACCESS=$(cookie "$CUSTOMER" accessToken)
check "менеджер блокирует покупателя" "$(restrict "$MANAGER" "$ID_CUSTOMER" true 'проверка check-admin.sh')" "200 True"
check "причина и время в карточке" "$(admin_get "$ADMIN" "/auth/admin/users/$ID_CUSTOMER" 'r["restrictionReason"] + " " + str(r["restrictedAt"] is not None)')" "200 проверка check-admin.sh True"
check "повторная блокировка — тихий успех" "$(restrict "$MANAGER" "$ID_CUSTOMER" true)" "200 True"
check "сессии завершены: refresh покупателя — 401" "$(status "$CUSTOMER" POST /auth/refresh)" 401
check "новый вход не пускает" "$(starts "$(login "$WORK/blocked" /me/ 79990000001)" "keycloak: Учетная запись заблокирована")" "keycloak: Учетная запись заблокирована"
check "уже выданный access-токен заблокированного — 401 сразу" "$(curl -s -o /dev/null -w '%{http_code}' -H "Cookie: accessToken=$OLD_ACCESS" "$API/me/")" 401
check "менеджер не блокирует сотрудника — 403" "$(restrict "$MANAGER" "$ID_ADMIN" true)" "403 users.staff_forbidden"
check "себя заблокировать нельзя — 403" "$(restrict "$ADMIN" "$ID_ADMIN" true)" "403 users.self_restriction"
check "без флага restricted — 400" "$(curl -s -o /dev/null -w '%{http_code}' -b "$ADMIN" -X PUT -H 'Content-Type: application/json' -d '{}' "$API/auth/admin/users/$ID_CUSTOMER/restriction")" 400
check "админ разблокирует" "$(restrict "$ADMIN" "$ID_CUSTOMER" false)" "200 False"
check "причина очищена" "$(admin_get "$ADMIN" "/auth/admin/users/$ID_CUSTOMER" 'str(r["restrictionReason"])')" "200 None"
check "вход снова работает" "$(login "$WORK/unblocked" /me/ 79990000001)" /me/
status "$WORK/unblocked" POST /auth/logout > /dev/null

echo "4. Смена роли (право users.roles.manage)"
ADMIN="$WORK/admin2"; : > "$ADMIN"
login "$ADMIN" /me/ 1234567890 > /dev/null   # свежий вход — можно выдавать admin
role "$ADMIN" "$ID_CUSTOMER" customer > /dev/null   # на случай прошлого упавшего прогона
CUSTOMER="$WORK/customer3"; : > "$CUSTOMER"
login "$CUSTOMER" /me/ 79990000001 > /dev/null
check "покупатель → manager" "$(role "$ADMIN" "$ID_CUSTOMER" manager)" "200 manager"
# Умный рефреш (а не выход, как в боевом): старый токен — 401 access_changed, сессия жива, refresh отдаёт разницу.
check "старый токен покупателя — 401 access_changed" \
  "$(curl -s -o /dev/null -D "$WORK/h" -w '%{http_code}' -b "$CUSTOMER" "$API/me/") $(grep -io 'access_changed' "$WORK/h" | head -1)" "401 access_changed"
check "умный рефреш без перелогина: добавилась роль manager" \
  "$(curl -s -o "$WORK/refresh.json" -w '%{http_code}' -b "$CUSTOMER" -c "$CUSTOMER" -X POST "$API/auth/refresh") $(python3 -c 'import json,sys; print(",".join(json.load(open(sys.argv[1]))["result"]["changes"]["addedRoles"]))' "$WORK/refresh.json")" \
  "200 manager"
check "после рефреша доступен режим manager" "$(me "$CUSTOMER" availableModes | tr ',' '\n' | sort | paste -sd, -)" "customer,manager"
check "та же роль — без изменений" "$(role "$ADMIN" "$ID_CUSTOMER" manager)" "200 manager"
check "refresh после «той же роли» работает" "$(status "$CUSTOMER" POST /auth/refresh)" 200
check "выдача admin при свежем входе" "$(role "$ADMIN" "$ID_CUSTOMER" admin)" "200 admin"
check "обратно в покупатели" "$(role "$ADMIN" "$ID_CUSTOMER" customer)" "200 customer"
check "неизвестная роль — 400" "$(role "$ADMIN" "$ID_CUSTOMER" courier)" "400 value.is.invalid"
check "свою роль менять нельзя — 403" "$(role "$ADMIN" "$ID_ADMIN" customer)" "403 users.self_role"
check "менеджеру нельзя — 403" "$(curl -s -o /dev/null -w '%{http_code}' -b "$MANAGER" -X PUT -H 'Content-Type: application/json' -d '{"role":"manager"}' "$API/auth/admin/users/$ID_CUSTOMER/role")" 403
# Повторный вход: при живой SSO-сессии обычный вход проходит без формы, reauth=true — снова форма (пароль, 2FA).
curl -s -o /dev/null -D "$WORK/h" -b "$ADMIN" -c "$ADMIN" "$API/auth/login?return_to=/"
check "обычный вход при живой сессии — без формы" "$(curl -s -o /dev/null -w '%{http_code}' -b "$ADMIN" -c "$ADMIN" "$(header "$WORK/h" location)")" 302
curl -s -o /dev/null -D "$WORK/h" -b "$ADMIN" -c "$ADMIN" "$API/auth/login?return_to=/&reauth=true"
check "reauth=true — Keycloak снова показывает форму" "$(curl -s -o "$WORK/page.html" -b "$ADMIN" -c "$ADMIN" "$(header "$WORK/h" location)"; [ "$(kc_page "$WORK/page.html")" = login.ftl ] && echo форма || echo нет)" форма
status "$CUSTOMER" POST /auth/logout > /dev/null

echo "5. Доступ к приложению (аккаунт рабочий, одно приложение закрыто)"
app_access "$ADMIN" "$ID_CUSTOMER" sandbox-bff true > /dev/null   # на случай прошлого упавшего прогона
APPS='",".join(a["clientId"] + ":" + str(a["hasAccess"]) for a in r)'
check "приложения проекта и доступ" "$(admin_get "$ADMIN" "/auth/admin/users/$ID_CUSTOMER/apps" "$APPS")" "200 account-console:True,manager-1c:True,sandbox-bff:True"
CUSTOMER="$WORK/customer4"; : > "$CUSTOMER"
login "$CUSTOMER" /me/ 79990000001 > /dev/null
OLD_ACCESS=$(cookie "$CUSTOMER" accessToken)
check "менеджер закрывает покупателю демо-приложение" "$(app_access "$MANAGER" "$ID_CUSTOMER" sandbox-bff false)" "200 False"
check "личный кабинет остаётся открыт" "$(admin_get "$ADMIN" "/auth/admin/users/$ID_CUSTOMER/apps" "$APPS")" "200 account-console:True,manager-1c:True,sandbox-bff:False"
check "уже выданный access-токен демо-приложения — 401 сразу" "$(curl -s -o /dev/null -w '%{http_code}' -H "Cookie: accessToken=$OLD_ACCESS" "$API/me/")" 401
check "открытая сессия демо-приложения: refresh — 401" "$(status "$CUSTOMER" POST /auth/refresh)" 401
check "новый вход в демо-приложение — «доступ закрыт»" \
  "$(starts "$(login "$WORK/denied" /me/ 79990000001)" "keycloak: Доступ к этому приложению для вас закрыт")" "keycloak: Доступ к этому приложению для вас закрыт"
check "другое приложение (dev-cli) токены выдаёт" "$(curl -s -o /dev/null -w '%{http_code}' -d grant_type=password -d client_id=dev-cli \
  -d username=79990000001 -d 'password=Test1234!' "${KC:-http://localhost:8180}/realms/sandbox/protocol/openid-connect/token")" 200
check "аккаунт не заблокирован" "$(admin_get "$ADMIN" "/auth/admin/users/$ID_CUSTOMER" 'str(r["isRestricted"])')" "200 False"
check "не приложение (sandbox-api) — 404" "$(app_access "$ADMIN" "$ID_CUSTOMER" sandbox-api false)" "404 apps.not_found"
check "свой доступ — 403" "$(app_access "$ADMIN" "$ID_ADMIN" sandbox-bff false)" "403 users.self_access"
check "менеджер сотруднику — 403" "$(app_access "$MANAGER" "$ID_ADMIN" sandbox-bff false)" "403 users.staff_forbidden"
check "открыть доступ обратно" "$(app_access "$ADMIN" "$ID_CUSTOMER" sandbox-bff true)" "200 True"
check "вход в демо-приложение снова работает" "$(login "$WORK/reopened" /me/ 79990000001)" /me/
status "$WORK/reopened" POST /auth/logout > /dev/null

echo "6. Сессии (/security/sessions и «выйти везде»)"
PHONE="$WORK/phone"; LAPTOP="$WORK/laptop"; : > "$PHONE"; : > "$LAPTOP"
login "$PHONE" /me/ 79990000003 > /dev/null
login "$LAPTOP" /me/ 79990000003 > /dev/null
CURRENT='[i["id"] for i in r["items"] if i["isCurrent"]][0]'
PHONE_SID=$(my_sessions "$PHONE" "$CURRENT")
LAPTOP_SID=$(my_sessions "$LAPTOP" "$CURRENT")
LAPTOP_ACCESS=$(cookie "$LAPTOP" accessToken)
check "в списке свои сессии, текущая отмечена" "$(my_sessions "$PHONE" 'str(sum(i["isCurrent"] for i in r["items"])) + " " + str(any(i["id"] == "'"$LAPTOP_SID"'" for i in r["items"]))')" "1 True"
check "в сессии видно приложение" "$(my_sessions "$PHONE" '",".join([i for i in r["items"] if i["isCurrent"]][0]["apps"])')" sandbox-bff
check "с телефона завершить сессию ноутбука" "$(post "$PHONE" "/security/sessions/$LAPTOP_SID/terminate" 'str(any(i["id"] == "'"$LAPTOP_SID"'" for i in r["items"]))')" "200 False"
check "ноутбук: access-токен — 401 сразу" "$(curl -s -o /dev/null -w '%{http_code}' -H "Cookie: accessToken=$LAPTOP_ACCESS" "$API/me/")" 401
check "ноутбук: refresh — 401" "$(status "$LAPTOP" POST /auth/refresh)" 401
check "телефон: refresh — 200" "$(status "$PHONE" POST /auth/refresh)" 200
ADMIN_SID=$(my_sessions "$ADMIN" "$CURRENT")
check "чужую сессию — 404" "$(post "$PHONE" "/security/sessions/$ADMIN_SID/terminate" 'r')" "404 sessions.not_found"
: > "$LAPTOP"; login "$LAPTOP" /me/ 79990000003 > /dev/null
check "завершить все остальные" "$(post "$PHONE" /security/sessions/terminate-all-others 'str(r["terminated"] >= 1)')" "200 True"
check "ноутбук снова отключён" "$(status "$LAPTOP" POST /auth/refresh)" 401
check "телефон работает" "$(status "$PHONE" POST /auth/refresh)" 200
ID_MANAGER_CUSTOMER=$(curl -s -b "$ADMIN" "$API/auth/admin/users?search=79990000003" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["items"][0]["id"])')
check "админ видит сессии пользователя" "$(admin_get "$ADMIN" "/auth/admin/users/$ID_MANAGER_CUSTOMER/sessions" 'str(len(r["items"]) >= 1)')" "200 True"
PHONE_ACCESS=$(cookie "$PHONE" accessToken)
check "менеджеру «выйти везде» нельзя — 403" "$(status "$MANAGER" POST "/auth/admin/users/$ID_MANAGER_CUSTOMER/logout")" 403
check "админ: «выйти везде»" "$(post "$ADMIN" "/auth/admin/users/$ID_MANAGER_CUSTOMER/logout" 'str(len(r["items"]))')" "200 0"
check "access-токен телефона — 401 сразу" "$(curl -s -o /dev/null -w '%{http_code}' -H "Cookie: accessToken=$PHONE_ACCESS" "$API/me/")" 401
check "телефон: refresh — 401" "$(status "$PHONE" POST /auth/refresh)" 401

echo "7. Сессию завершили в самом Keycloak — сервис узнаёт об этом (back-channel logout)"
TABLET="$WORK/tablet"; : > "$TABLET"
login "$TABLET" /me/ 79990000003 > /dev/null
TABLET_SID=$(my_sessions "$TABLET" "$CURRENT")
check "до: access-токен работает" "$(status "$TABLET" GET /me/)" 200
# Мастер-админка Keycloak удаляет сессию напрямую, мимо нашего API; Keycloak шлёт logout_token на /auth/backchannel-logout.
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
MASTER=$(master_token)
check "сессия удалена в админке Keycloak" "$(curl -s -o /dev/null -w '%{http_code}' -X DELETE -H "Authorization: Bearer $MASTER" "$KC/admin/realms/sandbox/sessions/$TABLET_SID")" 204
sleep 1
check "после: access-токен — 401 (Keycloak сообщил сервису)" "$(status "$TABLET" GET /me/)" 401

echo "8. Журнал: действия сотрудников и входы"
ADMIN="$WORK/admin3"; MANAGER="$WORK/manager3"; : > "$ADMIN"; : > "$MANAGER"
login "$ADMIN" /me/ 1234567890 > /dev/null
login "$MANAGER" /me/ 0987654321 > /dev/null
# Только действия разделов 3–5: у покупателя в журнале есть и другие (сброс пароля из раздела 9 прошлых прогонов).
ACTIONS='",".join(sorted({i["action"] for i in r["items"]} & {"UserAppAccessChanged", "UserRestrictionChanged", "UserRoleChanged"}))'
check "действия с покупателем записаны (разделы 3–5)" \
  "$(admin_get "$ADMIN" "/auth/admin/audit?objectId=$ID_CUSTOMER&pageSize=100" "$ACTIONS")" \
  "200 UserAppAccessChanged,UserRestrictionChanged,UserRoleChanged"
BLOCK='[i for i in r["items"] if i["action"] == "UserRestrictionChanged" and i["changes"]["restricted"]["new"]][0]'
check "блокировка: кто, в каком режиме, причина" \
  "$(admin_get "$ADMIN" "/auth/admin/audit?objectId=$ID_CUSTOMER&action=UserRestrictionChanged&pageSize=100" "$BLOCK"'["actorName"] + " " + '"$BLOCK"'["actorRole"] + " " + '"$BLOCK"'["changes"]["reason"]')" \
  "200 +70987654321 manager проверка check-admin.sh"
check "смена роли: было → стало" \
  "$(admin_get "$ADMIN" "/auth/admin/audit?objectId=$ID_CUSTOMER&action=UserRoleChanged&pageSize=1" 'r["items"][0]["changes"]["role"]["old"] + "→" + r["items"][0]["changes"]["role"]["new"]')" \
  "200 admin→customer"
check "«выйти везде» записано" \
  "$(admin_get "$ADMIN" "/auth/admin/audit?objectId=$ID_MANAGER_CUSTOMER&action=UserLoggedOutEverywhere&pageSize=1" 'r["items"][0]["objectName"]')" \
  "200 79990000003"
check "фильтр по автору: действия менеджера" \
  "$(admin_get "$ADMIN" "/auth/admin/audit?actorId=$ID_MANAGER&pageSize=100" 'str(all(i["actorName"] == "+70987654321" for i in r["items"]) and len(r["items"]) > 0)')" \
  "200 True"
check "менеджеру журнал недоступен — 403" "$(status "$MANAGER" GET /auth/admin/audit)" 403
login "$WORK/wrong" /me/ 79990000001 wrong-password > /dev/null   # неудачный вход — событие LOGIN_ERROR
check "входы покупателя: LOGIN и LOGIN_ERROR" \
  "$(admin_get "$ADMIN" "/auth/admin/login-events?userId=$ID_CUSTOMER&pageSize=100" '",".join(sorted({i["type"] for i in r["items"]} & {"LOGIN", "LOGIN_ERROR"}))')" \
  "200 LOGIN,LOGIN_ERROR"
check "ошибка входа с причиной" \
  "$(admin_get "$ADMIN" "/auth/admin/login-events?userId=$ID_CUSTOMER&pageSize=100" '[i for i in r["items"] if i["type"] == "LOGIN_ERROR"][0]["error"]')" \
  "200 invalid_user_credentials"
check "в карточке — последний вход" "$(admin_get "$ADMIN" "/auth/admin/users/$ID_CUSTOMER" 'str(r["lastLoginAt"] is not None)')" "200 True"

echo "9. Пароль и 2FA (право users.credentials.manage — только админ проекта)"
MAILPIT="${MAILPIT:-http://localhost:${MAILPIT_UI_PORT:-8025}}"
ID_OTP=$(curl -s -b "$ADMIN" "$API/auth/admin/users?search=79990000006" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["items"][0]["id"])')
# post_admin <jar> <метод> <путь> — запрос к API админки без тела; печатает «<код> <hasTwoFactor или код ошибки>»
post_admin() {
  curl -s -o "$WORK/admin.json" -w '%{http_code}' -b "$1" -X "$2" "$API$3"
  printf ' '
  python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); print(d["result"]["hasTwoFactor"] if d.get("result") else d["error"]["messages"][0]["code"])' "$WORK/admin.json"
}
# setup_otp <логин> — вход с обязательным действием «настроить 2FA»: секрет — со страницы Keycloak, код — по нему;
# печатает, куда сервис вернул браузер
setup_otp() {
  local jar="$WORK/otp-setup" page="$WORK/page.html" h="$WORK/h" secret
  : > "$jar"
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=/me/"
  curl -s -o "$page" -b "$jar" -c "$jar" "$(header "$h" location)"
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" --data-urlencode "username=$1" --data-urlencode "password=$PASSWORD" "$(form_action "$page")"
  curl -s -o "$page" -b "$jar" -c "$jar" "$(header "$h" location)"
  secret=$(kc "$page" 'c["totp"]["totpSecret"]')
  [ -n "$secret" ] || { echo "нет страницы настройки 2FA"; return; }
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" --data-urlencode "totp=$(totp "$secret")" --data-urlencode "totpSecret=$secret" \
    --data-urlencode "userLabel=check-admin" "$(form_action "$page")"
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$(header "$h" location)"
  header "$h" location
}
curl -s -o /dev/null -X DELETE "$MAILPIT/api/v1/search?query=to:customer@sandbox.test"
check "сброс пароля: письмо отправлено" "$(post_admin "$ADMIN" POST "/auth/admin/users/$ID_CUSTOMER/password-reset")" "200 False"
check "в письме — ссылка Keycloak на смену пароля" "$(for _ in 1 2 3 4 5 6 7 8 9 10; do
  link=$(curl -s "$MAILPIT/api/v1/search?query=to:customer@sandbox.test" | python3 -c '
import html,json,re,sys,urllib.request
m=json.load(sys.stdin)["messages"]
if m:
    body=json.load(urllib.request.urlopen(sys.argv[1] + "/api/v1/message/" + m[0]["ID"]))
    print("yes" if any("action-token" in l for l in re.findall(r"href=\"([^\"]+)\"", body["HTML"] or "")) else "")' "$MAILPIT")
  [ -n "$link" ] && { echo "$link"; break; }; sleep 1; done)" yes
check "ссылка из админки — письмо темы sandbox «Обновите учётную запись»" "$(curl -s "$MAILPIT/api/v1/search?query=to:customer@sandbox.test" | python3 -c 'import json,sys; print(json.load(sys.stdin)["messages"][0]["Subject"])')" "Обновите учётную запись"
check "в карточке — 2FA включена (79990000006)" "$(admin_get "$ADMIN" "/auth/admin/users/$ID_OTP" 'str(r["hasTwoFactor"])')" "200 True"
check "2FA нет — отключение без изменений" "$(post_admin "$ADMIN" DELETE "/auth/admin/users/$ID_CUSTOMER/two-factor")" "200 False"
# 2FA вживую: мастер-админка выдаёт покупателю обязательное действие «настроить 2FA», он настраивает приложение-
# аутентификатор при входе, админ отключает 2FA через наш API.
MASTER=$(master_token)
curl -s -o /dev/null -X PUT -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' \
  -d '{"requiredActions":["CONFIGURE_TOTP"]}' "$KC/admin/realms/sandbox/users/$ID_CUSTOMER"
check "покупатель настроил 2FA при входе" "$(setup_otp 79990000001)" /me/
check "в карточке — 2FA включена" "$(admin_get "$ADMIN" "/auth/admin/users/$ID_CUSTOMER" 'str(r["hasTwoFactor"])')" "200 True"
check "вход теперь спрашивает код" "$(: > "$WORK/otp-login"; login "$WORK/otp-login" /me/ 79990000001)" "keycloak: осталась форма"
check "админ отключил 2FA" "$(post_admin "$ADMIN" DELETE "/auth/admin/users/$ID_CUSTOMER/two-factor")" "200 False"
check "вход снова без кода" "$(: > "$WORK/otp-login"; login "$WORK/otp-login" /me/ 79990000001)" /me/
check "в журнале — 2FA отключена" \
  "$(admin_get "$ADMIN" "/auth/admin/audit?objectId=$ID_CUSTOMER&action=UserTwoFactorDisabled&pageSize=1" 'str(r["items"][0]["changes"]["removed"] > 0)')" \
  "200 True"
check "себе — нельзя (403)" "$(post_admin "$ADMIN" DELETE "/auth/admin/users/$ID_ADMIN/two-factor")" "403 users.self_credentials"
check "менеджеру — нельзя (403)" "$(status "$MANAGER" POST "/auth/admin/users/$ID_CUSTOMER/password-reset")" 403
check "в журнале — запрос сброса пароля" \
  "$(admin_get "$ADMIN" "/auth/admin/audit?objectId=$ID_CUSTOMER&action=UserPasswordResetRequested&pageSize=1" 'r["items"][0]["changes"]["email"]')" \
  "200 customer@sandbox.test"

for JAR in "$ADMIN" "$MANAGER"; do status "$JAR" POST /auth/logout > /dev/null; done

summary
