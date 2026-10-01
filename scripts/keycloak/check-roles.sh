#!/usr/bin/env bash
# Проверка этапа 4а (docs/keycloak/04a-roles.md): роли и права приложений в Keycloak.
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

require_service
ADMIN="$WORK/admin"; MANAGER="$WORK/manager"; : > "$ADMIN"; : > "$MANAGER"
login "$ADMIN" /me/ 1234567890 > /dev/null
login "$MANAGER" /me/ 0987654321 > /dev/null
PERMS=/auth/admin/apps/sandbox-api/permissions
ROLES=/auth/admin/apps/sandbox-api/roles
MANAGER_PERMS='["profile.view","users.block","users.view"]'
# тело — в переменной: внутри "$(...)" старый bash (macOS) режет такую строку по запятой
MANAGER_BODY="{\"description\":\"Менеджер\",\"permissions\":$MANAGER_PERMS}"
# на случай прошлого упавшего прогона
call "$ADMIN" DELETE "$ROLES/support" > /dev/null
call "$ADMIN" PUT "$ROLES/manager" "$MANAGER_BODY" > /dev/null
call "$ADMIN" DELETE "$PERMS/reports.export" > /dev/null

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
check "менеджеру нельзя — 403" "$(status "$MANAGER" GET "$ROLES")" 403

for JAR in "$ADMIN" "$MANAGER"; do status "$JAR" POST /auth/logout > /dev/null; done
summary
