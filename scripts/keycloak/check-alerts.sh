#!/usr/bin/env bash
# Проверка этапа 13.3: оповещения (deploy/monitoring/alerts.yml, alertmanager.yml).
#  1. Статически: promtool check rules, модульные тесты правил на искусственных рядах (alerts_test.yml), amtool check-config и
#     проверка маршрутов (critical → дежурному, warning → команде).
#  2. Рабочие Prometheus и Alertmanager стенда (профиль monitoring) загрузили правила и видят друг друга.
#  3. Живые сбои на стенде — быстрый временный Prometheus (опрос раз в 2 с, «for» правил — 4 с) и Alertmanager, письма — в Mailpit:
#     перебор паролей, коды без ввода, остановка Redis, базы, Keycloak и сервиса, исчерпание бюджета кодов. Для каждого оповещения
#     проверяется, что оно загорелось, дошло письмом и погасло, когда сбой убрали; оповещения-следствия при упавшем Keycloak подавляются.
#     Режим «закрыто» (Redis остановлен — 503), недоступная SmartCaptcha и «зависшая» база (пауза контейнера) тоже проверяются на стенде.
#     Только модульным тестом проверено AuthDependencyChecksStale: метка времени проверок застывает лишь при дефекте самой публикации, а
#     зависшая база даёт не «застыло», а «недоступна» (AuthDatabaseUnavailable) — это проверено живым сбоем.
# Нужен стенд (docker compose up -d --wait). Скрипт останавливает и запускает Redis, Postgres, Keycloak и сервис — и возвращает их.
# Запуск: scripts/keycloak/check-alerts.sh
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

MON="$ROOT/deploy/monitoring"
MAILPIT="${MAILPIT:-http://localhost:${MAILPIT_UI_PORT:-8025}}"
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
PROM_TEST_PORT=19090
AM_TEST_PORT=19093
PROM_TEST="http://127.0.0.1:$PROM_TEST_PORT"
dc() { docker compose --project-directory "$ROOT" "$@"; }
promtool() { docker run --rm -v "$MON:/m:ro" --entrypoint promtool prom/prometheus:v3.5.0 "$@"; }
amtool() { docker run --rm -v "$MON:/m:ro" --entrypoint amtool prom/alertmanager:v0.28.1 "$@"; }

require_service

echo "1. Правила и маршруты — статически"
check "promtool: правила корректны" "$(promtool check rules /m/alerts.yml 2>&1 | grep -c 'SUCCESS')" 1
TESTS=$(promtool test rules /m/alerts_test.yml 2>&1)
check "модульные тесты правил на искусственных рядах (alerts_test.yml)" "$(echo "$TESTS" | grep -c 'SUCCESS')" 1
[ "$(echo "$TESTS" | grep -c 'SUCCESS')" = 1 ] || echo "$TESTS" | head -20 | sed 's/^/    /'
RULES=$(grep -c '^      - alert:' "$MON/alerts.yml")
CASES=$(grep -c '^  - name:' "$MON/alerts_test.yml")
check "каждое правило имеет модульный тест (правил: $RULES)" \
  "$(for name in $(grep -o '^      - alert: [A-Za-z]*' "$MON/alerts.yml" | awk '{print $3}'); do grep -q "alertname: $name" "$MON/alerts_test.yml" || echo "$name"; done | tr '\n' ' ' | sed 's/ $//')" ""
for file in alertmanager.yml alertmanager.stand.yml; do
  check "amtool: $file корректен" "$(amtool check-config "/m/$file" 2>&1 | grep -c 'SUCCESS')" 1
done
check "маршрут: critical → on-call" "$(amtool config routes test --config.file=/m/alertmanager.yml severity=critical alertname=AuthRedisUnavailable 2>&1 | tail -1)" on-call
check "маршрут: Watchdog → внешний сторож (watchdog)" "$(amtool config routes test --config.file=/m/alertmanager.yml severity=none alertname=Watchdog 2>&1 | tail -1)" watchdog
check "маршрут: бюджет исчерпан → дежурному без писем «RESOLVED»" "$(amtool config routes test --config.file=/m/alertmanager.yml severity=critical alertname=AuthCodeBudgetExhausted 2>&1 | tail -1)" on-call-once
check "маршрут: warning → team-mail" "$(amtool config routes test --config.file=/m/alertmanager.yml severity=warning alertname=AuthLoginFailuresSpike 2>&1 | tail -1)" team-mail

echo "2. Рабочие Prometheus и Alertmanager стенда загрузили правила"
# --no-deps: сервис и Keycloak не пересоздаются (иначе счётчики сбросились бы посреди проверки)
dc --profile monitoring up -d --force-recreate --no-deps prometheus alertmanager > /dev/null 2>&1
PROM_STAND="http://127.0.0.1:${PROMETHEUS_PORT:-9090}"
for _ in $(seq 40); do [ "$(command curl -s -o /dev/null -w '%{http_code}' "$PROM_STAND/-/ready")" = 200 ] && break; sleep 2; done
for _ in $(seq 20); do
  [ "$(command curl -s "$PROM_STAND/api/v1/alertmanagers" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)["data"]["activeAlertmanagers"]))' 2>/dev/null)" = 1 ] && break
  sleep 2
done
for _ in $(seq 30); do
  [ "$(command curl -s "$PROM_STAND/api/v1/rules" | python3 -c 'import json,sys; print(sum(1 for g in json.load(sys.stdin)["data"]["groups"] for r in g["rules"] if r.get("health") != "ok"))' 2>/dev/null)" = 0 ] && break
  sleep 2
done
check "Prometheus стенда: правил загружено" \
  "$(command curl -s "$PROM_STAND/api/v1/rules" | python3 -c 'import json,sys; print(sum(len(g["rules"]) for g in json.load(sys.stdin)["data"]["groups"]))')" "$RULES"
check "Prometheus стенда: Alertmanager подключён" \
  "$(command curl -s "$PROM_STAND/api/v1/alertmanagers" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)["data"]["activeAlertmanagers"]))')" 1
check "Prometheus стенда: правила выполняются без ошибок" \
  "$(command curl -s "$PROM_STAND/api/v1/rules" | python3 -c 'import json,sys; print(sum(1 for g in json.load(sys.stdin)["data"]["groups"] for r in g["rules"] if r.get("health") != "ok"))')" 0

# Рабочая пара (профиль monitoring) дальше не нужна и мешала бы: она помнит сбои в своих окнах в 5–15 минут и слала бы письма в тот же Mailpit.
dc --profile monitoring stop prometheus alertmanager > /dev/null 2>&1

echo "3. Живые сбои: временные Prometheus и Alertmanager"
# Keycloak перезапускается до старта временного Prometheus: счётчики плагина (бюджет) создаются заново и в его данных ряда ещё нет —
# первое событие бюджета придёт рядом, «появившимся» за последние минуты, как и в бою.
dc restart keycloak > /dev/null 2>&1
dc up -d --wait keycloak > /dev/null 2>&1
NETWORK=$(docker inspect -f '{{range $k, $_ := .NetworkSettings.Networks}}{{$k}} {{end}}' "$(dc ps -q keycloak)" | awk '{print $1}')
T="$WORK/live"; mkdir -p "$T"
cat > "$T/prometheus.yml" <<YAML
global: { scrape_interval: 2s, evaluation_interval: 2s }
alerting:
  alertmanagers:
    - static_configs: [{ targets: ["check-alerts-am:9093"] }]
rule_files: [/etc/prometheus/alerts.yml]
scrape_configs:
  - { job_name: auth-service, static_configs: [{ targets: ["auth-service:9100"] }] }
  - { job_name: keycloak, metrics_path: /metrics, static_configs: [{ targets: ["keycloak:9000"] }] }
YAML
# Правила те же, что в бою, но «for» — 4 секунды: иначе каждый сбой ждали бы минутами.
sed -E 's/^( *for: )[0-9]+m/\14s/' "$MON/alerts.yml" > "$T/alerts.yml"
# AuthMetricsMissing в бою ждёт дольше, чем AuthServiceDown (5 минут против 1), и потому подавляется им. С одинаковыми 4 секундами
# оно могло дойти до Alertmanager первым и уйти письмом до подавления, — сохраняем порядок.
sed -i.bak -E '/alert: AuthMetricsMissing/,/for:/ s/^( *for: )4s/\120s/' "$T/alerts.yml" && rm -f "$T/alerts.yml.bak"
sed -e 's/group_wait: 10s/group_wait: 2s/' -e 's/group_interval: 1m/group_interval: 10s/' "$MON/alertmanager.stand.yml" > "$T/alertmanager.yml"
cleanup() {
  docker rm -f check-alerts-prom check-alerts-am > /dev/null 2>&1
  dc unpause postgres > /dev/null 2>&1
  dc up -d --wait redis postgres > /dev/null 2>&1
  dc up -d --wait keycloak auth-service > /dev/null 2>&1
  # Настройки проекта из сценария 3.9 не должны остаться, если скрипт прервали посреди него.
  if [ -n "${TOKEN:-}" ] && declare -f set_limits > /dev/null; then set_limits cooldowns= captcha-after= > /dev/null 2>&1 || true; fi
  rm -rf "$WORK"
}
trap cleanup EXIT
docker rm -f check-alerts-prom check-alerts-am > /dev/null 2>&1
docker run -d --name check-alerts-am --network "$NETWORK" -p "127.0.0.1:$AM_TEST_PORT:9093" -v "$T/alertmanager.yml:/etc/alertmanager/alertmanager.yml:ro" \
  prom/alertmanager:v0.28.1 --config.file=/etc/alertmanager/alertmanager.yml --storage.path=/alertmanager > /dev/null
docker run -d --name check-alerts-prom --network "$NETWORK" -p "127.0.0.1:$PROM_TEST_PORT:9090" \
  -v "$T/prometheus.yml:/etc/prometheus/prometheus.yml:ro" -v "$T/alerts.yml:/etc/prometheus/alerts.yml:ro" \
  prom/prometheus:v3.5.0 --config.file=/etc/prometheus/prometheus.yml --storage.tsdb.retention.time=1h > /dev/null
for _ in $(seq 40); do [ "$(command curl -s -o /dev/null -w '%{http_code}' "$PROM_TEST/-/ready")" = 200 ] && break; sleep 1; done
for _ in $(seq 30); do
  [ "$(command curl -s "$PROM_TEST/api/v1/targets" | python3 -c 'import json,sys; print(sum(1 for t in json.load(sys.stdin)["data"]["activeTargets"] if t["health"] == "up"))' 2>/dev/null)" = 2 ] && break
  sleep 2
done
check "временный Prometheus собирает сервис и Keycloak" \
  "$(command curl -s "$PROM_TEST/api/v1/targets" | python3 -c 'import json,sys; print(sum(1 for t in json.load(sys.stdin)["data"]["activeTargets"] if t["health"] == "up"))')" 2
# Письма оповещений прошлого прогона (тема «Auth…»): чужие письма Mailpit не трогаем.
command curl -s -X DELETE "$MAILPIT/api/v1/search?query=subject%3AAuth" > /dev/null
# Watchdog горит всегда (мониторинг жив) и уходит только «сторожу»: писем о нём нет.

# firing — названия горящих оповещений (через пробел)
firing() {
  command curl -s "$PROM_TEST/api/v1/alerts" | python3 -c '
import json, sys
print(" ".join(sorted({a["labels"]["alertname"] for a in json.load(sys.stdin)["data"]["alerts"] if a["state"] == "firing"})))' 2>/dev/null
}
# wait_firing <имя> [секунд] — ждёт, пока оповещение загорится
wait_firing() {
  local i
  for ((i = 0; i < ${2:-60}; i += 2)); do
    [[ " $(firing) " == *" $1 "* ]] && return 0
    sleep 2
  done
  return 1
}
# wait_resolved <имя> [секунд] — ждёт, пока оповещение погаснет
wait_resolved() {
  local i
  for ((i = 0; i < ${2:-90}; i += 2)); do
    [[ " $(firing) " == *" $1 "* ]] || return 0
    sleep 2
  done
  return 1
}
# mail_with <название> — сколько писем Alertmanager о нём пришло в Mailpit
mail_with() {
  command curl -s "$MAILPIT/api/v1/search?limit=200&query=$(python3 -c 'import sys,urllib.parse; print(urllib.parse.quote(sys.argv[1]))' "subject:$1")" |
    python3 -c 'import json,sys; print(json.load(sys.stdin)["messages_count"])'
}
# wait_mail <название> — ждёт письмо о названии
wait_mail() {
  local i
  for ((i = 0; i < ${2:-40}; i += 2)); do
    [ "$(mail_with "$1")" -gt 0 ] && return 0
    sleep 2
  done
  return 1
}
# alert_ok <название> [ждать секунд] — горит, письмо пришло
alert_ok() {
  if wait_firing "$1" "${2:-60}"; then ok "$1: загорелось"; else bad "$1: не загорелось за ${2:-60} с (горит: $(firing))"; fi
  if wait_mail "$1"; then ok "$1: письмо пришло в Mailpit"; else bad "$1: письма нет"; fi
}
resolved_ok() { if wait_resolved "$1" "${2:-90}"; then ok "$1: погасло, когда сбой убрали"; else bad "$1: не погасло (горит: $(firing))"; fi; }

IP_BASE="10.$((RANDOM % 250))"
curl() { command curl -H "$CSRF_HEADER" -H "X-Forwarded-For: $IP" "$@"; }
IP="$IP_BASE.1.1"

echo "3.0 Сторож и подавление"
wait_firing Watchdog 30 && ok "Watchdog горит (мониторинг жив)" || bad "Watchdog не горит"

echo "3.1 Перебор паролей"
for i in $(seq 90); do
  IP="$IP_BASE.$((i % 250)).$((i / 250 + 1))"
  : > "$WORK/bf"
  n=$(( (RANDOM * 32768 + RANDOM) % 10000000 )) # в «$(…)» RANDOM у подоболочки начинается с одного состояния: номер считаем здесь
  login_until_done "$WORK/bf" /me/ "7996$(printf '%07d' "$n")" "wrong-password-$i" > /dev/null
done
alert_ok AuthLoginFailuresSpike
alert_ok AuthLoginFailureShareHigh

echo "3.2 Коды отправлены, никто их не вводит"
for i in $(seq 90); do
  IP="$IP_BASE.$((i % 250)).$((i / 250 + 100))"
  : > "$WORK/cj"
  n=$(( (RANDOM * 32768 + RANDOM) % 10000000 ))
  code_start "$WORK/cj" "+7998$(printf '%07d' "$n")" > /dev/null
done
alert_ok AuthCodeVerifiedShareLow

echo "3.3 Redis остановлен"
JAR="$WORK/session"; : > "$JAR"
login "$JAR" /me/ 0987654321 > /dev/null
dc stop redis > /dev/null 2>&1
alert_ok AuthRedisUnavailable
for _ in 1 2 3; do command curl -s -o /dev/null -b "$JAR" "$API/me/"; sleep 1; done
# Ошибки отзыва при лежащем Redis — следствие: письмо подавлено правилом (AuthRedisUnavailable → AuthRevocationStoreErrors), горит.
wait_firing AuthRevocationStoreErrors 60 && ok "AuthRevocationStoreErrors: загорелось" || bad "AuthRevocationStoreErrors: не загорелось"
sleep 12
check "подавление: об ошибках отзыва письма нет, пока Redis лежит" "$(mail_with AuthRevocationStoreErrors)" 0
dc up -d --wait redis > /dev/null 2>&1
resolved_ok AuthRedisUnavailable

echo "3.4 База остановлена"
dc stop postgres > /dev/null 2>&1
alert_ok AuthDatabaseUnavailable
dc up -d --wait postgres > /dev/null 2>&1
resolved_ok AuthDatabaseUnavailable
# База «зависла» (не отвечает, но не остановлена: сеть, диск): проверка здоровья обрывается по таймауту — «недоступна», а не «жива, потому что молчит».
dc pause postgres > /dev/null 2>&1
alert_ok AuthDatabaseUnavailable 120
dc unpause postgres > /dev/null 2>&1
resolved_ok AuthDatabaseUnavailable 120

echo "3.5 Keycloak остановлен, сервис пробует обновить токены"
dc stop keycloak > /dev/null 2>&1
alert_ok AuthKeycloakDown
for _ in $(seq 60); do command curl -s -o /dev/null -X POST -H "$CSRF_HEADER" -H 'Cookie: refreshToken=not-a-real-token' "$API/auth/refresh"; done
wait_firing AuthRefreshKeycloakErrors 60 && ok "AuthRefreshKeycloakErrors: загорелось" || bad "AuthRefreshKeycloakErrors: не загорелось"
wait_firing AuthKeycloakErrors 30 && ok "AuthKeycloakErrors: загорелось" || bad "AuthKeycloakErrors: не загорелось"
wait_firing AuthRefreshFailures 30 && ok "AuthRefreshFailures: загорелось" || bad "AuthRefreshFailures: не загорелось"
# Следствия при упавшем Keycloak подавлены: письма о них не уходят (ждём, пока Alertmanager отработает всё), письмо об упавшем Keycloak — есть.
sleep 15
check "подавление: об ошибках Keycloak письма нет, пока Keycloak лежит" "$(mail_with AuthKeycloakErrors)" 0
check "подавление: об ошибках обновления письма нет" "$(mail_with AuthRefreshFailures)" 0
check "подавление: о сбоях обновления в Keycloak письма нет" "$(mail_with AuthRefreshKeycloakErrors)" 0
dc up -d --wait keycloak > /dev/null 2>&1
resolved_ok AuthKeycloakDown 120

echo "3.6 Бюджет кодов: 80 % и 100 %"
# Keycloak только что перезапущен: его счётчики и отметка «уже оповестили» (в памяти) сброшены — оповещения о бюджете гаснут, а
# check-budget-alerts доводит расход до 80 и 100 % и зажигает их заново (без этого «горит» могло бы остаться от прошлого прогона).
resolved_ok AuthCodeBudgetNearlyExhausted 120
resolved_ok AuthCodeBudgetExhausted 120
(cd "$ROOT" && bash scripts/keycloak/check-budget-alerts.sh) > "$WORK/budget.log" 2>&1
check "check-budget-alerts довёл расход до 80 и 100 % (код выхода)" "$?" 0
alert_ok AuthCodeBudgetNearlyExhausted 40
alert_ok AuthCodeBudgetExhausted 40

echo "3.7 Сервис остановлен"
dc stop auth-service > /dev/null 2>&1
alert_ok AuthServiceDown
# Метрик готовности нет, пока сервис лежит: AuthMetricsMissing горит, но письмо подавлено (следствие AuthServiceDown).
wait_firing AuthMetricsMissing 60 && ok "AuthMetricsMissing: загорелось (метрики пропали)" || bad "AuthMetricsMissing: не загорелось"
dc up -d --wait auth-service > /dev/null 2>&1
resolved_ok AuthServiceDown 120
resolved_ok AuthMetricsMissing 120
echo "3.8 Режим «закрыто»: Redis остановлен — сервис отвечает 503"
cat > "$T/closed.yaml" <<'YAML'
services:
  auth-service:
    environment:
      TokenRevocation__OnStoreFailure: Closed
YAML
dcx() { docker compose --project-directory "$ROOT" -f "$ROOT/compose.yaml" "$@"; }
dcx -f "$T/closed.yaml" up -d --force-recreate --no-deps --wait auth-service > /dev/null 2>&1
for _ in $(seq 30); do [ "$(command curl -s -o /dev/null -w '%{http_code}' "$API/health/live")" = 200 ] && break; sleep 1; done
JAR="$WORK/closed"; : > "$JAR"
login "$JAR" /me/ 0987654321 > /dev/null
dc stop redis > /dev/null 2>&1
CODES=""
for _ in 1 2 3 4; do CODES="$CODES $(command curl -s -o /dev/null -w '%{http_code}' -b "$JAR" "$API/me/")"; sleep 1; done
check "в режиме «закрыто» запросы получают 503" "$(echo "$CODES" | tr ' ' '\n' | grep -c 503)" 4
alert_ok AuthTokenChecksUnavailable
# Оповещение смотрит на окно в 5 минут: после возврата Redis оно горит ещё до пяти минут — гашения не ждём.
dc up -d --wait redis > /dev/null 2>&1
dc up -d --force-recreate --no-deps --wait auth-service > /dev/null 2>&1

echo "3.9 SmartCaptcha недоступна"
cat > "$T/captcha.yaml" <<'YAML'
services:
  keycloak:
    environment:
      KC_SPI_AUTHENTICATOR__PLATFORM_CODE_LOGIN__CAPTCHA_SERVER_KEY: check-alerts-key
      KC_SPI_AUTHENTICATOR__PLATFORM_CODE_LOGIN__CAPTCHA_URL: http://captcha-down.invalid/validate
YAML
dcx -f "$T/captcha.yaml" up -d --force-recreate --no-deps --wait keycloak > /dev/null 2>&1
TOKEN=$(master_token)
set_limits() {
  python3 - "$@" > "$WORK/attrs.json" <<'PY'
import json, sys
attrs = {"platform.codes." + k: v for k, v in (a.split("=", 1) for a in sys.argv[1:])}
print(json.dumps({"attributes": attrs}))
PY
  put_realm_attributes "$KC/admin/realms/sandbox" "$TOKEN" "$WORK/attrs.json" > /dev/null
}
set_limits cooldowns=1 captcha-after=1
IP="$IP_BASE.200.200"
n=$(( (RANDOM * 32768 + RANDOM) % 10000000 ))
CJ="$WORK/captcha"; : > "$CJ"
code_start "$CJ" "+7998$(printf '%07d' "$n")" > /dev/null
sleep 1.2
for attempt in 1 2 3; do
  # С токеном капчи: проверка уходит в SmartCaptcha, а её адрес не существует — «недоступна»; код всё равно уходит (captcha-on-outage=pass).
  command curl -s -o "$WORK/page.html" -H "$CSRF_HEADER" -H "X-Forwarded-For: $IP" -b "$CJ" -c "$CJ" --data-urlencode "resend=" --data-urlencode "smart-token=any-token" "$(form_action "$WORK/page.html")"
  sleep 1.2
done
set_limits cooldowns= captcha-after=
alert_ok AuthCaptchaUnavailable 90
dc up -d --force-recreate --no-deps --wait keycloak > /dev/null 2>&1

check "письма о Watchdog не уходят на почту" "$(mail_with Watchdog)" 0
check "подавление: об AuthMetricsMissing письма нет" "$(mail_with AuthMetricsMissing)" 0

summary
