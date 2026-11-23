#!/usr/bin/env bash
# Проверка этапа 13.4: ограничения частоты запросов к /auth/login, /auth/refresh и /auth/services/*.
# Сначала (при высоких лимитах стенда) админ проекта выдаёт два ключа сервисов. Потом сервис пересоздаётся с низкими лимитами
# (временный override compose) и скрипт проверяет: 429 с Retry-After и обычным телом ошибки, лимит по IP клиента (за прокси — по
# X-Forwarded-For), лимит по ключу сервиса (чужой ключ не задет), запросы без токена сервиса считаются по IP, обычный вход и другие адреса
# не затронуты, после ожидания Retry-After запросы снова проходят, в метриках есть отказы без адресов и ключей.
# Сервис возвращается к обычным лимитам стенда, ключи отзываются.
# Нужны: стенд, разрешение на регистрацию ключей (scripts/keycloak/registration-token.sh --write-env).
# Запуск: scripts/keycloak/check-rate-limits.sh
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
TOKEN_URL="$KC/realms/sandbox/protocol/openid-connect/token"
METRICS="${METRICS:-http://127.0.0.1:${AUTH_METRICS_PORT:-9100}}"
WINDOW=10
CODE_A=check-rl-a
CODE_B=check-rl-b
dc() { docker compose --project-directory "$ROOT" "$@"; }

# cx <ip> <curl-аргументы> — запрос так, как его пересылает прокси: настоящий IP клиента в X-Forwarded-For
cx() { local ip=$1; shift; command curl -s -H "$CSRF_HEADER" -H "X-Forwarded-For: $ip" "$@"; }
# code <ip> <метод> <путь> [заголовок Authorization] — только код ответа
code() { cx "$1" -o /dev/null -w '%{http_code}' -X "$2" ${4:+-H "$4"} "$API$3"; }
# repeat <n> <ip> <метод> <путь> [заголовок] — коды n запросов подряд через пробел
repeat() { local n=$1 i out=""; shift; for ((i = 0; i < n; i++)); do out="$out$(code "$@") "; done; echo "${out% }"; }

admin_call() {
  local args=(-s -o "$WORK/r.json" -w '%{http_code}' -b "$1" -c "$1" -X "$2")
  [ -n "${4:-}" ] && args+=(-H 'Content-Type: application/json' -d "$4")
  printf '%s ' "$(curl "${args[@]}" "$API$3")"
  python3 -c '
import json,sys
t=open(sys.argv[1]).read().strip(); d=json.loads(t) if t else {}
print(json.dumps(d["result"], ensure_ascii=False) if d.get("result") is not None else (d.get("error") or {}).get("messages", [{}])[0].get("code", ""))' "$WORK/r.json"
}
field() { python3 -c 'import json,sys; v=json.loads(sys.argv[1].split(" ",1)[1]); print(eval(sys.argv[2]))' "$1" "$2"; }
access_token() {
  command curl -s -d grant_type=client_credentials -d "client_id=$1" --data-urlencode "client_secret=$2" "$TOKEN_URL" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])'
}

# wait_for <секунды> <ожидаемый код> <curl-аргументы> — ждёт, пока адрес ответит нужным кодом (после перезапуска Keycloak и сервис греются дольше, чем
# думает «healthy» в compose)
wait_for() {
  local seconds=$1 expected=$2 i code=""; shift 2
  for ((i = 0; i < seconds; i++)); do
    code=$(command curl -s -o /dev/null -w '%{http_code}' "$@")
    [ "$code" = "$expected" ] && return 0
    sleep 1
  done
  return 1
}

require_service

ADMIN="$WORK/admin"; : > "$ADMIN"
cleanup() {
  dc up -d --force-recreate --no-deps --wait auth-service > /dev/null 2>&1
  wait_for 60 200 "$API/health/live"
  local jar="$WORK/cleanup"; : > "$jar"
  if [ "$(login "$jar" /me/ 1234567890)" = /me/ ]; then
    for c in "$CODE_A" "$CODE_B"; do admin_call "$jar" DELETE "/auth/admin/services/$c" > /dev/null; done
  fi
  rm -rf "$WORK"
}
trap cleanup EXIT

echo "1. Два ключа сервисов (при обычных лимитах стенда)"
# Стенд мог только что перезапускаться (предыдущая проверка пересоздаёт Keycloak и сервис): ждём, пока вход действительно заработает.
wait_for 180 200 "$KC/realms/sandbox/.well-known/openid-configuration" || bad "Keycloak не отвечает"
wait_for 120 302 "$API/auth/login?return_to=/me/" || bad "сервис не отдаёт редирект на вход"
check "админ проекта вошёл" "$(login "$ADMIN" /me/ 1234567890)" /me/
for c in "$CODE_A" "$CODE_B"; do admin_call "$ADMIN" DELETE "/auth/admin/services/$c" > /dev/null; done
BODY_A="{\"code\":\"$CODE_A\",\"name\":\"Проверка лимитов A\",\"permissions\":[\"service.users.read\"]}"
BODY_B="{\"code\":\"$CODE_B\",\"name\":\"Проверка лимитов B\",\"permissions\":[\"service.users.read\"]}"
RA=$(admin_call "$ADMIN" POST /auth/admin/services "$BODY_A")
RB=$(admin_call "$ADMIN" POST /auth/admin/services "$BODY_B")
check "ключи выданы" "$(echo "$RA" | cut -d' ' -f1)$(echo "$RB" | cut -d' ' -f1)" 200200
TOKEN_A=$(access_token "service-$CODE_A" "$(field "$RA" 'v["clientSecret"]')")
TOKEN_B=$(access_token "service-$CODE_B" "$(field "$RB" 'v["clientSecret"]')")
AUTH_A="Authorization: Bearer $TOKEN_A"
AUTH_B="Authorization: Bearer $TOKEN_B"

echo "2. Сервис с низкими лимитами: вход 5, refresh 3, сервисы 5, без токена сервиса 3 — за $WINDOW секунд"
cat > "$WORK/limits.yaml" <<YAML
services:
  auth-service:
    environment:
      RateLimiting__Login__PermitLimit: "5"
      RateLimiting__Login__WindowSeconds: "$WINDOW"
      RateLimiting__Refresh__PermitLimit: "3"
      RateLimiting__Refresh__WindowSeconds: "$WINDOW"
      RateLimiting__Services__PermitLimit: "5"
      RateLimiting__Services__WindowSeconds: "$WINDOW"
      RateLimiting__ServicesAnonymous__PermitLimit: "3"
      RateLimiting__ServicesAnonymous__WindowSeconds: "$WINDOW"
      # Только на время проверки: curl с хоста приходит через шлюз Docker (на macOS — вне доверенных сетей по умолчанию), а IP клиентов
      # здесь задаются заголовком X-Forwarded-For. В бою доверяют только своему прокси (ForwardedHeaders:TrustedNetworks).
      ForwardedHeaders__TrustedNetworks__0: 0.0.0.0/0
      ForwardedHeaders__TrustedNetworks__1: "::/0"
YAML
dc -f "$ROOT/compose.yaml" -f "$WORK/limits.yaml" up -d --force-recreate --no-deps --wait auth-service > "$WORK/up.log" 2>&1 || tail -5 "$WORK/up.log"
wait_for 60 200 "$API/health/live" && ok "сервис поднялся с низкими лимитами" || bad "сервис не поднялся с низкими лимитами"
# Токены не зависят от перезапуска сервиса, а окна — от начала прогона: каждый блок ниже работает со своим адресом или ключом.

echo "3. /auth/login — по IP клиента"
IP1=203.0.113.11; IP2=203.0.113.12
check "5 запросов проходят (редирект на вход)" "$(repeat 5 $IP1 GET "/auth/login?return_to=/me/")" "302 302 302 302 302"
HEAD="$WORK/h"
cx $IP1 -o "$WORK/body.json" -D "$HEAD" "$API/auth/login?return_to=/me/" > /dev/null
check "шестой — 429" "$(head -1 "$HEAD" | cut -d' ' -f2)" 429
RETRY=$(header "$HEAD" retry-after)
check "Retry-After — число секунд не больше окна" "$([ "${RETRY:-0}" -ge 1 ] && [ "${RETRY:-0}" -le "$WINDOW" ] && echo да || echo "нет ($RETRY)")" да
check "в ответе X-Trace-Id" "$([ -n "$(header "$HEAD" x-trace-id)" ] && echo да || echo нет)" да
check "тело — обычная ошибка: RATE_LIMIT, auth.rate_limited, isError" "$(python3 -c '
import json,sys
d=json.load(open(sys.argv[1])); e=d["error"]
print(e["type"], e["messages"][0]["code"], d["isError"])' "$WORK/body.json")" "RATE_LIMIT auth.rate_limited True"
check "другой IP (X-Forwarded-For) не задет" "$(code $IP2 GET "/auth/login?return_to=/me/")" 302
check "обычный вход человека целиком проходит (свой IP)" "$(
  curl() { command curl -H "$CSRF_HEADER" -H "X-Forwarded-For: 203.0.113.13" "$@"; }
  login "$WORK/person" /me/ 0987654321)" /me/
sleep $((RETRY + 1))
check "после Retry-After запросы снова проходят" "$(code $IP1 GET "/auth/login?return_to=/me/")" 302

echo "4. /auth/refresh — по IP клиента"
check "3 запроса доходят до сервиса (401: нет сессии)" "$(repeat 3 $IP1 POST /auth/refresh)" "401 401 401"
check "четвёртый — 429" "$(code $IP1 POST /auth/refresh)" 429
check "другой IP не задет" "$(code $IP2 POST /auth/refresh)" 401

echo "5. /auth/services/* — по ключу сервиса"
USER_URL="/auth/services/users/00000000-0000-0000-0000-000000000001"
check "ключ A: 5 запросов проходят (404: такого человека нет)" "$(repeat 5 $IP1 GET $USER_URL "$AUTH_A")" "404 404 404 404 404"
check "ключ A: шестой — 429" "$(code $IP1 GET $USER_URL "$AUTH_A")" 429
check "ключ B с того же адреса не задет" "$(code $IP1 GET $USER_URL "$AUTH_B")" 404
check "ключ A с другого адреса тоже ограничен (лимит — на ключ)" "$(code $IP2 GET $USER_URL "$AUTH_A")" 429

echo "6. /auth/services/* без токена сервиса — по IP"
check "3 запроса без токена — 401" "$(repeat 3 $IP2 GET $USER_URL)" "401 401 401"
check "четвёртый — 429" "$(code $IP2 GET $USER_URL)" 429
check "мусорный токен считается так же (по IP): тоже 429" "$(code $IP2 GET $USER_URL 'Authorization: Bearer not-a-token')" 429
check "ключ B с адреса, засорённого мусором, работает" "$(code $IP2 GET $USER_URL "$AUTH_B")" 404

echo "7. Что не ограничивается"
check "остальные адреса сервиса не затронуты (/me/, 10 запросов)" "$(repeat 10 $IP1 GET /me/ | tr ' ' '\n' | sort -u | tr '\n' ' ')" "401 "
check "проверка готовности не ограничивается" "$(repeat 10 $IP1 GET /health/live | tr ' ' '\n' | sort -u | tr '\n' ' ')" "200 "

echo "8. Метрики"
M=$(command curl -s "$METRICS/metrics")
for policy in login refresh services services_anonymous; do
  check "auth_rate_limited_total{policy=\"$policy\"} больше нуля" \
    "$(echo "$M" | grep "^auth_rate_limited_total{policy=\"$policy\"" | awk '{ print ($NF > 0) ? "да" : "нет" }' | head -1)" да
done
check "в метриках лимитов нет адресов и ключей сервисов" \
  "$(echo "$M" | grep '^auth_rate_limited' | grep -cE '203\.0\.113|service-check-rl|check-rl')" 0

echo "9. Ограничение снимается ожиданием окна"
sleep $((WINDOW + 2))
check "после окна: ключ A снова работает" "$(code $IP1 GET $USER_URL "$AUTH_A")" 404
check "после окна: refresh снова доходит до сервиса" "$(code $IP1 POST /auth/refresh)" 401

summary
