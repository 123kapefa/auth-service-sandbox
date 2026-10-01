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

me() { curl -s -b "$1" "$API/me/" | python3 -c 'import json,sys; v=json.load(sys.stdin)["result"][sys.argv[1]]; print(",".join(v) if isinstance(v,list) else v)' "$2" 2>/dev/null; }

require_service

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
for JAR in "$ADMIN" "$MANAGER" "$CUSTOMER"; do status "$JAR" POST /auth/logout > /dev/null; done

summary
