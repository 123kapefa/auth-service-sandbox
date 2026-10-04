#!/usr/bin/env bash
# Проверка шага 6б.3 (docs/keycloak/06b-account-branding.md): раздел «Устройства» кабинета — где вошли (браузер
# записывается при входе), выйти на устройстве, на всех других, история входов (и неудачных) из событий Keycloak.
# Проверка заводит своего пользователя и удаляет его в конце.
# Запуск: стенд поднят (Docker: сервис :8080, Keycloak :8180), затем
#   scripts/keycloak/check-devices.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM=sandbox
PHONE=79990000097
MAC_CHROME='Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36'
IPHONE_SAFARI='Mozilla/5.0 (iPhone; CPU iPhone OS 18_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Mobile/15E148 Safari/604.1'

MASTER=$(master_token)
admin_api() { curl -s -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' "$@"; }
user_id() { admin_api "$KC/admin/realms/$REALM/users?username=$PHONE&exact=true" | python3 -c 'import json,sys; u=json.load(sys.stdin); print(u[0]["id"] if u else "")'; }
remove_user() { local id; id=$(user_id); [ -z "$id" ] || admin_api -X DELETE "$KC/admin/realms/$REALM/users/$id" > /dev/null; }
create_user() {
  local json
  json=$(python3 -c 'import json,sys; print(json.dumps({"username": sys.argv[1], "enabled": True, "attributes": {"phone": ["+" + sys.argv[1]]},
  "credentials": [{"type": "password", "value": sys.argv[2], "temporary": False}]}))' "$PHONE" "$PASSWORD")
  admin_api -o /dev/null -w '%{http_code}' -d "$json" "$KC/admin/realms/$REALM/users"
}
trap 'remove_user; rm -rf "$WORK"' EXIT

# login_as <jar> <User-Agent> — вход «с устройства»: callback сервиса — с User-Agent браузера; печатает, куда вернул
login_as() {
  local url
  url=$(login_until_done "$1" /account/ "$PHONE" "$PASSWORD") || { echo "$url"; return; }
  curl -s -o /dev/null -D "$WORK/h" -A "$2" -b "$1" -c "$1" "$url"
  header "$WORK/h" location
}

# sessions <jar> <python-выражение над r> — свои сессии (GET /security/sessions), logins — история (GET /security/logins)
sessions() { curl -s -b "$1" "$API/security/sessions" | python3 -c "import json,sys; r=json.load(sys.stdin)['result']['items']; print($2)"; }
logins() { curl -s -b "$1" "$API/security/logins" | python3 -c "import json,sys; r=json.load(sys.stdin)['result']['items']; print($2)"; }
me() { curl -s -o /dev/null -w '%{http_code}' -b "$1" "$API/me/profile"; }

require_service
echo "0. Подготовка"
remove_user
check "пользователь проверки заведён" "$(create_user)" 201
check "/auth/account/devices ведёт в раздел «Устройства»" \
  "$(curl -s -o /dev/null -w '%{redirect_url}' "$API/auth/account/devices")" "$API/account/devices"
check "без входа — 401" "$(curl -s -o /dev/null -w '%{http_code}' "$API/security/logins")" 401

echo "1. Где вошли"
LAPTOP="$WORK/laptop"; PHONE_JAR="$WORK/phone"; : > "$LAPTOP"; : > "$PHONE_JAR"
check "неверный пароль — вход не состоялся" \
  "$(login_until_done "$WORK/bad" /account/ "$PHONE" 'Wrong1234!' > /dev/null; echo $?)" 1
check "вход с ноутбука" "$(login_as "$LAPTOP" "$MAC_CHROME")" /account/
check "вход с телефона" "$(login_as "$PHONE_JAR" "$IPHONE_SAFARI")" /account/
check "две сессии: это устройство — Chrome · macOS, другое — Safari · iPhone" \
  "$(sessions "$LAPTOP" 'sorted((s["isCurrent"], s["deviceLabel"]) for s in r)')" \
  "[(False, 'Safari · iPhone'), (True, 'Chrome · macOS')]"

echo "2. История входов"
check "входы с устройствами и неудачная попытка с причиной, новые сверху" \
  "$(logins "$LAPTOP" '[(i["success"], i["deviceLabel"], i["error"]) for i in r]')" \
  "[(True, 'Safari · iPhone', None), (True, 'Chrome · macOS', None), (False, None, 'invalid_user_credentials')]"

echo "3. Выйти на устройстве, на всех других"
PHONE_SESSION=$(sessions "$LAPTOP" 'next(s["id"] for s in r if not s["isCurrent"])')
check "выйти на телефоне с ноутбука" "$(status "$LAPTOP" POST "/security/sessions/$PHONE_SESSION/terminate")" 200
check "телефон сразу без доступа (токен отозван)" "$(me "$PHONE_JAR")" 401
check "ноутбук — на месте" "$(sessions "$LAPTOP" 'len(r)') $(me "$LAPTOP")" "1 200"
: > "$PHONE_JAR"
check "снова вошли с телефона" "$(login_as "$PHONE_JAR" "$IPHONE_SAFARI")" /account/
check "выйти на всех других — одна сессия" \
  "$(curl -s -X POST -b "$LAPTOP" "$API/security/sessions/terminate-all-others" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["terminated"])')" 1
check "телефон без доступа, ноутбук — с доступом" "$(me "$PHONE_JAR") $(me "$LAPTOP")" "401 200"

summary
