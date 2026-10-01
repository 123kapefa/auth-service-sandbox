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

summary
