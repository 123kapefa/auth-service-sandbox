#!/usr/bin/env bash
# Проверка этапа 4а (docs/keycloak/04a-roles.md): роли и права приложений в Keycloak, роли пользователей.
# Запуск: сервис и Keycloak подняты, затем
#   scripts/keycloak/check-roles.sh                            # Docker: http://localhost:8080
#   API=http://localhost:5080 scripts/keycloak/check-roles.sh    # Development: dotnet run
# Скрипт убирает за собой созданные права и роли — можно гонять подряд.
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

# call <jar> <метод> <путь> [json] [python-выражение над result] — печатает «<код> <значение или код ошибки>»
call() {
  local jar=$1 method=$2 path=$3 body=${4:-} expr=${5:-'"ok"'}
  if [ -n "$body" ]; then
    curl -s -o "$WORK/r.json" -w '%{http_code}' -b "$jar" -X "$method" -H 'Content-Type: application/json' -d "$body" "$API$path"
  else
    curl -s -o "$WORK/r.json" -w '%{http_code}' -b "$jar" -X "$method" "$API$path"
  fi
  printf ' '
  python3 -c '
import json,sys
try: d=json.load(open(sys.argv[1]))
except Exception: print("—"); sys.exit()
r=d.get("result")
print(eval(sys.argv[2]) if d.get("error") is None else d["error"]["messages"][0]["code"])' "$WORK/r.json" "$expr"
}

# mode <jar> <режим> — POST /auth/mode с сохранением новых cookie; печатает «<HTTP-код> <activeMode или код ошибки>»
mode() {
  curl -s -o "$WORK/r.json" -w '%{http_code}' -b "$1" -c "$1" -H 'Content-Type: application/json' -d "{\"activeMode\":\"$2\"}" "$API/auth/mode"
  printf ' '
  python3 -c '
import json,sys
d=json.load(open(sys.argv[1]))
print(d["result"]["activeMode"] if d.get("error") is None else d["error"]["messages"][0]["code"])' "$WORK/r.json"
}

# app_token <логин> <python-выражение над claims c> — токен «Менеджера 1С» (ТОЛЬКО DEV: клиент manager-1c-cli) и его claims
app_token() {
  curl -s -d grant_type=password -d client_id=manager-1c-cli -d "username=$1" -d 'password=Test1234!' \
    "${KC:-http://localhost:8180}/realms/sandbox/protocol/openid-connect/token" | python3 -c '
import base64,json,sys
t=json.load(sys.stdin).get("access_token")
if not t: print("—"); sys.exit()
p=t.split(".")[1]; c=json.loads(base64.urlsafe_b64decode(p + "=" * (-len(p) % 4)))
print(eval(sys.argv[1]))' "$2"
}

require_service
ADMIN="$WORK/admin"; MANAGER="$WORK/manager"; : > "$ADMIN"; : > "$MANAGER"
login "$ADMIN" /me/ 1234567890 > /dev/null
PERMS=/auth/admin/apps/sandbox-api/permissions
ROLES=/auth/admin/apps/sandbox-api/roles
MANAGER_PERMS='["profile.view","users.block","users.view"]'
# тело — в переменной: внутри "$(...)" старый bash (macOS) режет такую строку по запятой
MANAGER_BODY="{\"description\":\"Менеджер\",\"permissions\":$MANAGER_PERMS}"
# на случай прошлого упавшего прогона
call "$ADMIN" DELETE "$ROLES/support" > /dev/null
call "$ADMIN" PUT "$ROLES/manager" "$MANAGER_BODY" > /dev/null
call "$ADMIN" DELETE "$PERMS/reports.export" > /dev/null
CUSTOMER_ID=$(call "$ADMIN" GET '/auth/admin/users?search=79990000001' '' 'r["items"][0]["id"]' | cut -d' ' -f2)
ADMIN_ID=$(call "$ADMIN" GET '/auth/admin/users?search=71234567890' '' 'r["items"][0]["id"]' | cut -d' ' -f2)
USER_ROLES="/auth/admin/apps/sandbox-api/users/$CUSTOMER_ID/roles"
call "$ADMIN" PUT "/auth/admin/users/$CUSTOMER_ID/role" '{"role":"customer"}' > /dev/null
OPERATOR_ID=$(call "$ADMIN" GET '/auth/admin/users?search=79990000008' '' 'r["items"][0]["id"]' | cut -d' ' -f2)
call "$ADMIN" PUT "/auth/admin/apps/manager-1c/users/$OPERATOR_ID/roles" '{"roles":[]}' > /dev/null
call "$ADMIN" PUT "/auth/admin/apps/manager-1c/users/$OPERATOR_ID/roles" '{"roles":["operator"]}' > /dev/null   # и режим 1С — operator
call "$ADMIN" PUT "/auth/admin/users/$OPERATOR_ID/apps/manager-1c/access" '{"hasAccess":true}' > /dev/null
call "$ADMIN" DELETE "/auth/admin/apps/manager-1c/roles/logist" > /dev/null
call "$ADMIN" DELETE "/auth/admin/apps/manager-1c/permissions/orders.export" > /dev/null
login "$MANAGER" /me/ 0987654321 > /dev/null   # после очистки: состав manager уже не изменится до раздела 2

echo "1. Каталог прав: системные из кода и свои из админки"
check "системные права приложения (из кода, source=code)" \
  "$(call "$ADMIN" GET "$PERMS" '' '",".join(sorted(p["name"] for p in r if p["source"] == "code"))')" \
  "200 audit.view,profile.view,roles.manage,users.block,users.roles.manage,users.sessions.manage,users.view"
check "у права есть описание" "$(call "$ADMIN" GET "$PERMS" '' '[p["description"] for p in r if p["name"] == "users.view"][0]')" "200 Пользователи: список и карточка"
check "своё право создаётся (source=admin)" \
  "$(call "$ADMIN" POST "$PERMS" '{"name":"reports.export","description":"Выгрузка отчётов"}' '[p["source"] for p in r if p["name"] == "reports.export"][0]')" "200 admin"
check "повторно — 409" "$(call "$ADMIN" POST "$PERMS" '{"name":"reports.export"}')" "409 roles.exists"
check "имя не по правилам — 400" "$(call "$ADMIN" POST "$PERMS" '{"name":"Reports Export"}')" "400 invalid.format"
check "системное право удалить нельзя — 409" "$(call "$ADMIN" DELETE "$PERMS/users.view")" "409 permissions.system"
check "роль через права не удалить — 400" "$(call "$ADMIN" DELETE "$PERMS/manager")" "400 permissions.not_permission"
check "своё право удаляется" "$(call "$ADMIN" DELETE "$PERMS/reports.export" '' 'str(any(p["name"] == "reports.export" for p in r))')" "200 False"
check "в журнале — создание и удаление права" \
  "$(call "$ADMIN" GET '/auth/admin/audit?pageSize=5' '' '",".join(i["action"] for i in r["items"][:2])')" "200 PermissionDeleted,PermissionCreated"
check "не приложение (sandbox-bff) — 404" "$(call "$ADMIN" GET /auth/admin/apps/sandbox-bff/permissions)" "404 apps.not_found"
check "менеджеру нельзя — 403" "$(status "$MANAGER" GET "$PERMS")" 403

echo "2. Роли приложения и их права"
ROLE_PERMS='",".join(sorted([x for x in r if x["name"] == "%s"][0]["permissions"]))'
check "встроенные роли" "$(call "$ADMIN" GET "$ROLES" '' '",".join(sorted(x["name"] for x in r if x["builtin"]))')" "200 admin,customer,manager"
check "права manager" "$(call "$ADMIN" GET "$ROLES" '' "$(printf "$ROLE_PERMS" manager)")" "200 profile.view,users.block,users.view"
check "создать роль support с правами" \
  "$(call "$ADMIN" POST "$ROLES" '{"name":"support","description":"Поддержка","permissions":["users.view","profile.view"]}' "$(printf "$ROLE_PERMS" support)")" \
  "200 profile.view,users.view"
check "роль в Keycloak — составная из этих прав" \
  "$(curl -s -H "Authorization: Bearer $(curl -s -d grant_type=password -d client_id=admin-cli -d username=admin --data-urlencode "password=$(grep -s '^KEYCLOAK_ADMIN_PASSWORD=' "$(dirname "$0")/../../.env" | cut -d= -f2)" "${KC:-http://localhost:8180}/realms/master/protocol/openid-connect/token" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')" \
     "${KC:-http://localhost:8180}/admin/realms/sandbox/clients/2631bc45-b176-4774-927a-b6dd4e310582/roles/support/composites" | python3 -c 'import json,sys; print(",".join(sorted(r["name"] for r in json.load(sys.stdin))))')" \
  "profile.view,users.view"
check "неизвестное право — 400" "$(call "$ADMIN" POST "$ROLES" '{"name":"helpdesk","permissions":["no.such"]}')" "400 roles.unknown_permission"
check "имя с точкой — 400 (так зовут права)" "$(call "$ADMIN" POST "$ROLES" '{"name":"support.team"}')" "400 invalid.format"
check "изменить состав manager (ровно этот список)" \
  "$(call "$ADMIN" PUT "$ROLES/manager" '{"description":"Менеджер","permissions":["profile.view","users.view","audit.view"]}' "$(printf "$ROLE_PERMS" manager)")" \
  "200 audit.view,profile.view,users.view"
check "у admin не убрать roles.manage — 409" "$(call "$ADMIN" PUT "$ROLES/admin" '{"permissions":["profile.view"]}')" "409 roles.lockout"
check "встроенную роль не удалить — 409" "$(call "$ADMIN" DELETE "$ROLES/manager")" "409 roles.builtin"
check "удалить свою роль" "$(call "$ADMIN" DELETE "$ROLES/support" '' 'str(any(x["name"] == "support" for x in r))')" "200 False"
check "в журнале — создание, изменение и удаление ролей" \
  "$(call "$ADMIN" GET '/auth/admin/audit?pageSize=10' '' '",".join(sorted({i["action"] for i in r["items"] if i["objectType"] == "role"}))')" \
  "200 RoleCreated,RoleDeleted,RoleUpdated"
check "состав manager — обратно" \
  "$(call "$ADMIN" PUT "$ROLES/manager" "$MANAGER_BODY" "$(printf "$ROLE_PERMS" manager)")" \
  "200 profile.view,users.block,users.view"
# Состав роли менялся — токены в режиме manager устарели: 401 access_changed, сессия жива, refresh — новые права.
check "токен менеджера — 401 access_changed" \
  "$(curl -s -o /dev/null -D "$WORK/h" -w '%{http_code}' -b "$MANAGER" "$API/me/") $(grep -io 'access_changed' "$WORK/h" | head -1)" "401 access_changed"
check "умный рефреш менеджера — без перелогина" "$(status "$MANAGER" POST /auth/refresh)" 200
check "права manager в новом токене" "$(call "$MANAGER" GET /me/ '' '",".join(r["permissions"])')" "200 profile.view,users.block,users.view"
check "менеджеру нельзя — 403" "$(status "$MANAGER" GET "$ROLES")" 403

echo "3. Роли пользователя: несколько ролей, роль = режим"
CUSTOMER="$WORK/customer"; : > "$CUSTOMER"
# роли|режимы|активный режим
USER_ROLES_OF='",".join(r["roles"]) + "|" + ",".join(r["modes"]) + "|" + str(r["activeMode"])'
call "$ADMIN" POST "$ROLES" '{"name":"support","description":"Поддержка","permissions":["users.view","profile.view"]}' > /dev/null
check "у покупателя — только роль по умолчанию" "$(call "$ADMIN" GET "$USER_ROLES" '' "$USER_ROLES_OF")" "200 |customer|customer"
check "выдать две роли" \
  "$(call "$ADMIN" PUT "$USER_ROLES" '{"roles":["manager","Support"]}' "$USER_ROLES_OF")" "200 manager,support|customer,manager,support|customer"
login "$CUSTOMER" /me/ 79990000001 > /dev/null
check "в токене — все роли режимами" "$(call "$CUSTOMER" GET /me/ '' '",".join(r["availableModes"])')" "200 customer,manager,support"
check "режим — своя роль support" "$(mode "$CUSTOMER" support)" "200 support"
check "права режима support в токене" "$(call "$CUSTOMER" GET /me/ '' '",".join(r["permissions"])')" "200 profile.view,users.view"
check "в режиме support — список пользователей (users.view)" "$(status "$CUSTOMER" GET /auth/admin/users)" 200
check "в режиме support — смена ролей недоступна" "$(status "$CUSTOMER" GET "$USER_ROLES")" 403
check "снять роль активного режима — режим сброшен на customer" \
  "$(call "$ADMIN" PUT "$USER_ROLES" '{"roles":["manager"]}' "$USER_ROLES_OF")" "200 manager|customer,manager|customer"
check "роли изменились — старый токен 401 access_changed" \
  "$(curl -s -o /dev/null -D "$WORK/h" -w '%{http_code}' -b "$CUSTOMER" "$API/me/") $(grep -io 'access_changed' "$WORK/h" | head -1)" "401 access_changed"
check "умный рефреш: снята роль support, режим — customer" \
  "$(curl -s -o "$WORK/refresh.json" -w '%{http_code}' -b "$CUSTOMER" -c "$CUSTOMER" -X POST "$API/auth/refresh") $(python3 -c '
import json,sys; c=json.load(open(sys.argv[1]))["result"]["changes"]; print(",".join(c["removedRoles"]) + "|" + c["activeMode"])' "$WORK/refresh.json")" \
  "200 support|customer"
check "после рефреша — права customer" "$(call "$CUSTOMER" GET /me/ '' '",".join(r["permissions"])')" "200 profile.view"
check "нет такой роли — 400" "$(call "$ADMIN" PUT "$USER_ROLES" '{"roles":["courier"]}')" "400 roles.unknown_role"
check "право — не роль (400)" "$(call "$ADMIN" PUT "$USER_ROLES" '{"roles":["users.view"]}')" "400 roles.unknown_role"
check "свои роли менять нельзя — 403" "$(call "$ADMIN" PUT "/auth/admin/apps/sandbox-api/users/$ADMIN_ID/roles" '{"roles":[]}')" "403 users.self_role"
check "не приложение (sandbox-bff) — 404" "$(call "$ADMIN" GET "/auth/admin/apps/sandbox-bff/users/$CUSTOMER_ID/roles")" "404 apps.not_found"
check "старый PUT …/role — одна роль, как в боевом" "$(call "$ADMIN" PUT "/auth/admin/users/$CUSTOMER_ID/role" '{"role":"customer"}' 'r["role"]')" "200 customer"
check "после него — только роль по умолчанию" "$(call "$ADMIN" GET "$USER_ROLES" '' "$USER_ROLES_OF")" "200 |customer|customer"
check "менеджеру нельзя — 403" "$(status "$MANAGER" GET "$USER_ROLES")" 403
call "$ADMIN" DELETE "$ROLES/support" > /dev/null

echo "4. Второе приложение «Менеджер 1С» и его админ"
APP_ADMIN="$WORK/app-admin"; : > "$APP_ADMIN"
APP=/auth/admin/apps/manager-1c
APP_ADMIN_ID=$(call "$ADMIN" GET '/auth/admin/users?search=79990000007' '' 'r["items"][0]["id"]' | cut -d' ' -f2)
SECTION_STARTED=$(date -u +%Y-%m-%dT%H:%M:%SZ)
check "токен 1С: свой audience, роль и права режима" \
  "$(app_token 79990000008 'c["aud"] + "|" + ",".join(c["role"]) + "|" + ",".join(c["permissions"]) + "|" + c["active_mode"]')" \
  "manager-1c|operator|orders.edit,orders.view|operator"
login "$APP_ADMIN" /me/ 79990000007 > /dev/null
check "админ 1С в сервисе — покупатель" "$(call "$APP_ADMIN" GET /me/ '' '",".join(r["permissions"])')" "200 profile.view"
check "админ 1С видит роли своего приложения" \
  "$(call "$APP_ADMIN" GET "$APP/roles" '' '",".join(sorted(x["name"] for x in r))')" "200 accountant,admin,operator"
check "роли приложения сервиса — 403" "$(call "$APP_ADMIN" GET /auth/admin/apps/sandbox-api/roles)" "403 apps.forbidden"
check "пользователи проекта — 403" "$(status "$APP_ADMIN" GET /auth/admin/users)" 403
check "блокировка аккаунта — 403" "$(curl -s -o /dev/null -w '%{http_code}' -b "$APP_ADMIN" -X PUT -H 'Content-Type: application/json' -d '{"restricted":true}' "$API/auth/admin/users/$OPERATOR_ID/restriction")" 403
check "админка: «кто я» — только своё приложение" \
  "$(call "$APP_ADMIN" GET /auth/admin/me '' '",".join(a["clientId"] + ":" + str(a["viaProject"]) for a in r["apps"])')" "200 manager-1c:False"
check "пользователи 1С — только с ролью в нём" \
  "$(call "$APP_ADMIN" GET "$APP/users" '' '",".join(u["username"] + ":" + "+".join(u["roles"]) for u in r["items"])')" \
  "200 79990000007:admin,79990000008:operator"
check "точный поиск по телефону — человек без роли" \
  "$(call "$APP_ADMIN" GET "$APP/users?search=%2B7%20999%20000-00-01" '' 'r["items"][0]["username"] + ":" + str(len(r["items"][0]["roles"]))')" "200 79990000001:0"
check "по части номера базу не перебрать" "$(call "$APP_ADMIN" GET "$APP/users?search=7999" '' 'str(len(r["items"]))')" "200 0"
check "своё право приложения" \
  "$(call "$APP_ADMIN" POST "$APP/permissions" '{"name":"orders.export","description":"Выгрузка заказов"}' '[p["source"] for p in r if p["name"] == "orders.export"][0]')" "200 admin"
check "своя роль приложения" \
  "$(call "$APP_ADMIN" POST "$APP/roles" '{"name":"logist","permissions":["orders.view","orders.export"]}' "$(printf "$ROLE_PERMS" logist)")" \
  "200 orders.export,orders.view"
check "выдать оператору роль logist" \
  "$(call "$APP_ADMIN" PUT "$APP/users/$OPERATOR_ID/roles" '{"roles":["operator","logist"]}' "$USER_ROLES_OF")" "200 logist,operator|logist,operator|operator"
check "новый токен 1С: обе роли и версия доступа" \
  "$(app_token 79990000008 '",".join(c["available_roles"]) + "|" + str(c.get("access_version") is not None)')" "logist,operator|True"
check "снять роль активного режима — режим 1С: оставшаяся logist" \
  "$(call "$APP_ADMIN" PUT "$APP/users/$OPERATOR_ID/roles" '{"roles":["logist"]}' "$USER_ROLES_OF") $(app_token 79990000008 'c["active_mode"] + "|" + ",".join(c["permissions"])')" \
  "200 logist|logist|logist logist|orders.export,orders.view"
check "свои роли в 1С менять нельзя — 403" \
  "$(call "$APP_ADMIN" PUT "$APP/users/$APP_ADMIN_ID/roles" '{"roles":[]}')" \
  "403 users.self_role"
check "в журнале — 4 действия админа 1С с ролями" \
  "$(call "$ADMIN" GET "/auth/admin/audit?actorId=$APP_ADMIN_ID&from=$SECTION_STARTED&pageSize=50" '' '",".join(sorted(i["action"] for i in r["items"]))')" \
  "200 PermissionCreated,RoleCreated,UserRoleChanged,UserRoleChanged"
check "админ 1С закрывает оператору доступ к 1С — видит только своё приложение" \
  "$(call "$APP_ADMIN" PUT "/auth/admin/users/$OPERATOR_ID/apps/manager-1c/access" '{"hasAccess":false}' '",".join(a["clientId"] + ":" + str(a["hasAccess"]) for a in r)')" \
  "200 manager-1c:False"
check "доступ закрыт — токен 1С не выдаётся" "$(app_token 79990000008 'c["aud"]')" "—"
check "в ролях пользователя — доступ закрыт" "$(call "$APP_ADMIN" GET "$APP/users/$OPERATOR_ID/roles" '' 'str(r["hasAccess"])')" "200 False"
check "доступ демо-приложения сервиса — 403" \
  "$(call "$APP_ADMIN" PUT "/auth/admin/users/$OPERATOR_ID/apps/sandbox-bff/access" '{"hasAccess":false}')" "403 apps.forbidden"
check "доступ открыт — токен 1С снова выдаётся" \
  "$(call "$APP_ADMIN" PUT "/auth/admin/users/$OPERATOR_ID/apps/manager-1c/access" '{"hasAccess":true}' > /dev/null; app_token 79990000008 'c["aud"]')" "manager-1c"
check "журнал 1С — только записи приложения" \
  "$(call "$APP_ADMIN" GET "$APP/audit?from=$SECTION_STARTED&pageSize=50" '' 'str(len(r["items"]) > 0 and all(i["app"] == "manager-1c" for i in r["items"]))')" "200 True"
check "журнал проекта админу 1С — 403" "$(status "$APP_ADMIN" GET /auth/admin/audit)" 403
check "журнал проекта по приложению (?app=)" \
  "$(call "$ADMIN" GET "/auth/admin/audit?app=manager-1c&from=$SECTION_STARTED&pageSize=50" '' 'str(len(r["items"]) > 0 and all(i["app"] == "manager-1c" for i in r["items"]))')" "200 True"
check "админ проекта управляет любым приложением" "$(call "$ADMIN" GET "$APP/roles" '' 'str(len(r) > 0)')" "200 True"
call "$ADMIN" PUT "$APP/users/$OPERATOR_ID/roles" '{"roles":["operator"]}' > /dev/null
call "$ADMIN" DELETE "$APP/roles/logist" > /dev/null
call "$ADMIN" DELETE "$APP/permissions/orders.export" > /dev/null

for JAR in "$ADMIN" "$MANAGER" "$CUSTOMER" "$APP_ADMIN"; do status "$JAR" POST /auth/logout > /dev/null; done
summary
