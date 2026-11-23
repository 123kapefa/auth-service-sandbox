#!/usr/bin/env bash
# Проверка этапа 13.2: единые логи и сквозная трассировка (сервис входа → Keycloak).
# Один запрос с известным идентификатором трассы (заголовок traceparent): идентификатор должен вернуться в заголовке X-Trace-Id и в
# теле ответа с ошибкой, найтись в строках JSON-журнала сервиса и в журнале Keycloak, а в Jaeger (по OTLP) — в spans обоих сервисов.
# Отдельно — что в журналах нет токенов, секретов клиентов, паролей и кодов входа.
# Нужен стенд (docker compose up -d --wait). Для части про Jaeger скрипт временно пересоздаёт сервис и Keycloak с отправкой трасс
# в коллектор OpenTelemetry (профиль monitoring: коллектор вырезает сведения о людях, затем Jaeger) и в конце возвращает их в обычный режим.
# Запуск: scripts/keycloak/check-tracing.sh
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

JAEGER="${JAEGER:-http://127.0.0.1:${JAEGER_PORT:-16686}}"
dc() { docker compose --project-directory "$ROOT" "$@"; }

require_service

# traced_request <идентификатор трассы> — запрос, который доходит до Keycloak (обновление токена с негодным refresh): ответ 401 с ошибкой
traced_request() {
  command curl -s -D "$WORK/h" -o "$WORK/b" -X POST -H "traceparent: 00-$1-00f067aa0ba902b7-01" \
    -H 'X-Requested-With: XMLHttpRequest' -H 'Cookie: refreshToken=not-a-real-refresh-token' "$API/auth/refresh"
}
json_field() { python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get(sys.argv[2], ""))' "$WORK/b" "$1"; }

T=$(python3 -c 'import uuid; print(uuid.uuid4().hex)')

echo "1. Идентификатор трассы в ответе"
traced_request "$T"
check "ответ — 401 с ошибкой (refresh негоден)" "$(head -1 "$WORK/h" | tr -d '\r' | cut -d' ' -f2)" 401
check "заголовок X-Trace-Id — идентификатор из traceparent" "$(header "$WORK/h" X-Trace-Id)" "$T"
check "в теле ответа с ошибкой — тот же traceId" "$(json_field traceId)" "$T"
check "успешный ответ несёт X-Trace-Id (без traceId в теле)" \
  "$(command curl -s -o /dev/null -D "$WORK/h2" "$API/health/live"; header "$WORK/h2" X-Trace-Id | grep -c -E '^[0-9a-f]{32}$')" 1
command curl -s -D "$WORK/h3" -o "$WORK/b3" -X POST -H 'Cookie: accessToken=x' "$API/auth/logout"
check "403 защиты от CSRF тоже несёт идентификатор" "$(header "$WORK/h3" X-Trace-Id | grep -c -E '^[0-9a-f]{32}$')" 1

echo "2. Одна трасса — записи сервиса и Keycloak"
sleep 2
SERVICE_LOG=$(dc logs --no-log-prefix auth-service 2>&1)
KEYCLOAK_LOG=$(dc logs --no-log-prefix keycloak 2>&1)
[ "$(echo "$SERVICE_LOG" | grep -c "$T")" -gt 0 ] && ok "в журнале сервиса есть запись с идентификатором трассы" || bad "в журнале сервиса нет идентификатора $T"
# Строка о запросе (категория AuthService.Request): рядом в той же области могут быть другие записи сервиса.
echo "$SERVICE_LOG" | grep "$T" | grep '"Category":"AuthService.Request"' | head -1 > "$WORK/svc-line.json"
check "запись сервиса: код проекта и маршрут без строки запроса" \
  "$(python3 -c '
import json,sys
line = json.load(open(sys.argv[1]))
scopes = {k: v for s in line.get("Scopes", []) for k, v in s.items()}
print(scopes.get("Project"), line["Message"].split(" in ")[0])' "$WORK/svc-line.json" 2>&1 | tail -1)" "sandbox HTTP POST /auth/refresh -> 401"
[ "$(echo "$KEYCLOAK_LOG" | grep -c "\"traceId\":\"$T\"")" -gt 0 ] && ok "в журнале Keycloak есть запись с тем же идентификатором (mdc.traceId)" || bad "в журнале Keycloak нет идентификатора $T"
check "это запись Keycloak о том же запросе (REFRESH_TOKEN_ERROR)" \
  "$(echo "$KEYCLOAK_LOG" | grep "\"traceId\":\"$T\"" | grep -c REFRESH_TOKEN_ERROR)" 1

echo "3. В журналах нет секретов"
# Прогон входа с паролем и кодом, чтобы в журналах было что проверять.
JAR="$WORK/t1"; : > "$JAR"
login "$JAR" /me/ 0987654321 > /dev/null
login_until_done "$WORK/t2" /me/ 0987654321 wrong-password-for-log-check > /dev/null
status "$JAR" POST /auth/logout > /dev/null
NEW="7998$(printf '%07d' $(( (RANDOM * 32768 + RANDOM) % 10000000 )))"
CJAR="$WORK/t3"; : > "$CJAR"
code_start "$CJAR" "+$NEW" > /dev/null
CODE=$(phone_code "$NEW")
code_submit "$CJAR" "$CODE" > /dev/null
sleep 2
SERVICE_LOG=$(dc logs --no-log-prefix auth-service 2>&1)
KEYCLOAK_LOG=$(dc logs --no-log-prefix keycloak 2>&1)
# Выражения — в переменных: bash 3.2 (macOS) режет «"$(… {10,} …)"» по запятой (docs/tech/pitfalls.md).
JWT_RE='eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}'
for name in "сервиса:$SERVICE_LOG" "Keycloak:$KEYCLOAK_LOG"; do
  log="${name#*:}"
  check "журнал ${name%%:*}: нет токенов (JWT)" "$(echo "$log" | grep -c -E "$JWT_RE")" 0
  check "журнал ${name%%:*}: нет пароля входа" "$(echo "$log" | grep -c -F -e "$PASSWORD" -e 'wrong-password-for-log-check')" 0
  check "журнал ${name%%:*}: нет секрета клиента" "$(echo "$log" | grep -c -i -E 'client_secret|development-bff-secret')" 0
done
check "журнал сервиса: нет кода входа и state из адреса возврата" "$(echo "$SERVICE_LOG" | grep -c -E 'callback\?code=|[?&]state=')" 0
check "журнал сервиса: нет номера телефона нового покупателя" "$(echo "$SERVICE_LOG" | grep -c -F "$NEW")" 0
check "журнал сервиса: нет refresh-токена из запроса" "$(echo "$SERVICE_LOG" | grep -c 'not-a-real-refresh-token')" 0

echo "4. Spans в Jaeger: сервис входа и Keycloak в одной трассе"
export AUTH_OTEL_ENDPOINT=http://otel-collector:4317 AUTH_TRACING_SAMPLER=parentbased_always_on
restore() {
  unset AUTH_OTEL_ENDPOINT AUTH_TRACING_SAMPLER
  dc up -d --force-recreate --no-deps --wait keycloak auth-service > /dev/null 2>&1
}
trap 'restore; rm -rf "$WORK"' EXIT
dc --profile monitoring up -d --force-recreate --no-deps jaeger otel-collector > /dev/null 2>&1
dc --profile monitoring up -d --force-recreate --no-deps --wait keycloak auth-service > /dev/null 2>&1
for _ in $(seq 60); do [ "$(command curl -s -o /dev/null -w '%{http_code}' "$JAEGER/api/services")" = 200 ] && break; sleep 2; done
for _ in $(seq 30); do [ "$(command curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")" = 200 ] && break; sleep 2; done
# Сразу после пересоздания Keycloak его первая отправка spans в коллектор может уйти раньше, чем соединение готово, и пропасть: поэтому до пяти
# попыток с новым идентификатором, каждая ждёт трассу до 30 секунд.
SERVICES=""
T2=""
for attempt in 1 2 3 4 5; do
  T2=$(python3 -c 'import uuid; print(uuid.uuid4().hex)')
  traced_request "$T2"
  [ "$attempt" = 1 ] && check "ответ несёт идентификатор трассы" "$(header "$WORK/h" X-Trace-Id)" "$T2"
  for _ in $(seq 15); do
    SERVICES=$(command curl -s "$JAEGER/api/traces/$T2" | python3 -c '
import json,sys
try:
    data = json.load(sys.stdin).get("data") or []
except Exception:
    data = []
names = sorted({p["serviceName"] for t in data for p in t["processes"].values()})
print(",".join(names))')
    [ "$SERVICES" = "auth-service,keycloak" ] && break
    sleep 2
  done
  [ "$SERVICES" = "auth-service,keycloak" ] && break
done
check "трасса $T2 в Jaeger: spans сервиса входа и Keycloak" "$SERVICES" "auth-service,keycloak"
check "в трассе — запрос сервиса и запрос к Keycloak (родитель — span сервиса)" \
  "$(command curl -s "$JAEGER/api/traces/$T2" | python3 -c '
import json,sys
data = (json.load(sys.stdin).get("data") or [{"processes": {}, "spans": []}])[0]
procs = {k: v["serviceName"] for k, v in data["processes"].items()}
spans = {s["spanID"]: s for s in data["spans"]}
child = [s for s in data["spans"] if procs[s["processID"]] == "keycloak"]
ok = any(c["references"] and spans.get(c["references"][0]["spanID"]) and procs[spans[c["references"][0]["spanID"]]["processID"]] == "auth-service" for c in child)
print("да" if ok else "нет")')" да

echo "5. В spans нет секретов и данных людей"
# Трафик с секретами в адресе и в теле: вход по паролю (телефон), неверный пароль, возврат со страницы входа с кодом и state,
# вход по коду на новый номер. Потом все трассы обоих сервисов из Jaeger ищутся на эти значения.
SECRET_CODE="secretcode$(python3 -c 'import uuid; print(uuid.uuid4().hex[:8])')"
SECRET_STATE="secretstate$(python3 -c 'import uuid; print(uuid.uuid4().hex[:8])')"
command curl -s -o /dev/null "$API/auth/callback?code=$SECRET_CODE&state=$SECRET_STATE&email=probe-$SECRET_CODE@example.test"
JAR="$WORK/t5"; : > "$JAR"
login "$JAR" /me/ 0987654321 > /dev/null
login_until_done "$WORK/t6" /me/ 0987654321 wrong-password-for-span-check > /dev/null
status "$JAR" POST /auth/logout > /dev/null
NEW2="7998$(printf '%07d' $(( (RANDOM * 32768 + RANDOM) % 10000000 )))"
CJAR="$WORK/t7"; : > "$CJAR"
code_start "$CJAR" "+$NEW2" > /dev/null
CODE2=$(phone_code "$NEW2")
code_submit "$CJAR" "$CODE2" > /dev/null
sleep 8
: > "$WORK/spans.json"
for name in auth-service keycloak; do command curl -s "$JAEGER/api/traces?service=$name&limit=1500&lookback=1h" >> "$WORK/spans.json"; done
check "в Jaeger есть трассы сервиса и Keycloak за прогон" "$([ "$(wc -c < "$WORK/spans.json")" -gt 5000 ] && echo да || echo нет)" да
# Без этого проверки «нет поля» ниже прошли бы впустую, если бы spans Keycloak вообще не дошли до Jaeger.
check "в Jaeger есть spans Keycloak (проверки ниже не пустые)" "$([ "$(grep -c -F '"kc.realmName"' "$WORK/spans.json")" -gt 0 ] && echo да || echo нет)" да
for secret in "$SECRET_CODE" "$SECRET_STATE" "0987654321" "$NEW2" "$CODE2" "wrong-password-for-span-check" "not-a-real-refresh-token" "$PASSWORD"; do
  # Короткое значение (код из 4–6 цифр) совпало бы с кусочком идентификатора или времени: ищем его только как отдельное слово.
  if [ "${#secret}" -lt 10 ]; then
    found=$(grep -c -E "(^|[^0-9A-Za-z])${secret}([^0-9A-Za-z]|$)" "$WORK/spans.json")
  else
    found=$(grep -c -F -e "$secret" "$WORK/spans.json")
  fi
  check "spans: нет «${secret:0:14}…»" "$found" 0
done
check "spans: нет токенов (JWT)" "$(grep -c -E "$JWT_RE" "$WORK/spans.json")" 0
# Keycloak пишет в spans события с логином, IP и идентификаторами человека; коллектор их вырезает (deploy/monitoring/otel-collector.yaml).
for field in kc.details.username ipAddress kc.userId kc.sessionId client.address user_agent.original url.query; do
  check "spans: нет поля $field" "$(grep -c -F "\"key\":\"$field\"" "$WORK/spans.json")" 0
done

summary
