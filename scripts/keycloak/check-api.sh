#!/usr/bin/env bash
# Проверка этапа 2 (docs/keycloak/02-token-api.md): матрица доступа на настоящих токенах Keycloak.
# Запуск: сервис и Keycloak подняты, затем
#   scripts/keycloak/check-api.sh                          # Docker: http://localhost:8080
#   API=http://localhost:5080 scripts/keycloak/check-api.sh  # Development: dotnet run
set -uo pipefail

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
API="${API:-http://localhost:8080}"
REALM=sandbox
PASS=0; FAIL=0

ROUTES=(/me/ /demo/admin-only/ /demo/manager-or-admin/ /demo/profile/ /demo/users/ /demo/users/block/ /demo/users/roles/ /demo/audit/)
HEADERS=(me admin-only mgr-or-admin profile users block roles audit)

token() {
  curl -s -d grant_type=password -d client_id=dev-cli -d "username=$1" -d 'password=Test1234!' \
    "$KC/realms/$REALM/protocol/openid-connect/token" |
    python3 -c 'import json,sys; print(json.load(sys.stdin).get("access_token",""))'
}

# printf %-Ns и ${#text} (без UTF-8-локали) считают байты, а кириллица — 2 байта на символ.
pad() { python3 -c 'import sys; print(sys.argv[1].ljust(int(sys.argv[2])), end="")' "$1" "$2"; }

# row <логин|-> <описание> <ожидаемые коды по ROUTES...>
row() {
  local user=$1 title=$2; shift 2
  local bearer="" line
  if [ "$user" != "-" ]; then
    bearer=$(token "$user")
    [ -n "$bearer" ] || { FAIL=$((FAIL+1)); printf '  \033[31m✗\033[0m %s: Keycloak не выдал токен\n' "$title"; return; }
  fi
  line="  $(pad "$title" 26)"
  local i=0 expected code
  for expected in "$@"; do
    if [ -n "$bearer" ]; then
      code=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $bearer" "$API${ROUTES[$i]}")
    else
      code=$(curl -s -o /dev/null -w '%{http_code}' "$API${ROUTES[$i]}")
    fi
    if [ "$code" = "$expected" ]; then
      PASS=$((PASS+1)); line+=$(printf ' \033[32m%-12s\033[0m' "$code")
    else
      FAIL=$((FAIL+1)); line+=$(printf ' \033[31m%-12s\033[0m' "$code≠$expected")
    fi
    i=$((i+1))
  done
  echo "$line"
}

echo "Сервис: $API · Keycloak: $KC"
if [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/health/live")" != 200 ]; then
  echo "Сервис не отвечает на $API/health/live"; exit 1
fi

printf '  %s' "$(pad пользователь 26)"; for h in "${HEADERS[@]}"; do printf ' %-12s' "$h"; done; echo
row 79990000001 "покупатель"                 200 403 403 200 403 403 403 403
row 79990000002 "админ, режим customer"      200 403 403 200 403 403 403 403
row 79990000003 "менеджер, режим customer"   200 403 403 200 403 403 403 403
row 71234567890 "админ, режим admin"         200 200 200 200 200 200 200 200
row 70987654321 "менеджер, режим manager"    200 403 200 200 200 200 403 403
row -           "без токена"                 401 401 401 401 401 401 401 401

echo
echo "Итого: $PASS ✓, $FAIL ✗"
[ "$FAIL" -eq 0 ]
