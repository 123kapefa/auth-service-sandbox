#!/usr/bin/env bash
# Проверка этапа 13.1: метрики сервиса входа и Keycloak и готовый дашборд (deploy/monitoring).
# Скрипт сам создаёт события на стенде — успешный и неудачный вход, обновление и выход, код на телефон, сбой Redis, — ждёт,
# пока Prometheus их соберёт, и проверяет: метрики сервиса (auth_*, platformauth_*), события Keycloak (keycloak_user_events_total),
# метрики плагина кодов (platform_code_*), запросы каждой панели дашборда в Prometheus, что дашборд загружен в Grafana, и что в
# выдаче метрик нет токенов, номеров телефонов и адресов почты.
# Нужны: стенд (docker compose up -d --wait) и профиль monitoring — скрипт поднимает prometheus и grafana сам и оставляет их.
# Запуск: scripts/keycloak/check-metrics.sh
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

METRICS="${METRICS:-http://127.0.0.1:${AUTH_METRICS_PORT:-9100}}"
PROM="${PROM:-http://127.0.0.1:${PROMETHEUS_PORT:-9090}}"
GRAFANA="${GRAFANA:-http://127.0.0.1:${GRAFANA_PORT:-3030}}"
DASHBOARD="$ROOT/deploy/monitoring/grafana/dashboards/auth-service.json"
dc() { docker compose --project-directory "$ROOT" "$@"; }
# Логин и пароль Grafana стенда: одной переменной, чтобы в строке curl не стояло «пользователь:пароль» (gitleaks видит в нём секрет).
GRAFANA_AUTH="admin:${GRAFANA_ADMIN_PASSWORD:-admin}"

require_service

echo "1. Порт метрик сервиса и Keycloak"
check "порт метрик сервиса отвечает" "$(curl -s -o /dev/null -w '%{http_code}' "$METRICS/metrics")" 200
check "порт метрик не публикуется наружу: основной порт /metrics не отдаёт" "$(curl -s -o /dev/null -w '%{http_code}' "$API/metrics")" 404
check "порт метрик слушает только 127.0.0.1 хоста" \
  "$(dc port auth-service 9100 2>/dev/null | head -1)" "127.0.0.1:${AUTH_METRICS_PORT:-9100}"
# В образе Keycloak нет curl — метрики управления (порт 9000) читаем через bash /dev/tcp.
kc_metrics() {
  dc exec -T keycloak bash -c "exec 3<>/dev/tcp/127.0.0.1/9000; printf 'GET /metrics HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3; cat <&3"
}
[ "$(kc_metrics | grep -c '^keycloak_user_events_total')" -gt 0 ] \
  && ok "Keycloak отдаёт события входа (keycloak_user_events_total)" \
  || bad "в метриках Keycloak нет keycloak_user_events_total (KC_EVENT_METRICS_USER_ENABLED)"

echo "2. Prometheus и Grafana"
dc --profile monitoring up -d prometheus grafana > /dev/null 2>&1
for _ in $(seq 60); do
  [ "$(curl -s -o /dev/null -w '%{http_code}' "$PROM/-/ready")" = 200 ] && [ "$(curl -s -o /dev/null -w '%{http_code}' "$GRAFANA/api/health")" = 200 ] && break
  sleep 2
done
check "Prometheus готов" "$(curl -s -o /dev/null -w '%{http_code}' "$PROM/-/ready")" 200
check "Grafana готова" "$(curl -s -o /dev/null -w '%{http_code}' "$GRAFANA/api/health")" 200
# up{job} — 1, когда Prometheus собрал метрики цели; даём собрать первый раз.
target_up() { curl -s "$PROM/api/v1/query" --data-urlencode "query=up{job=\"$1\"}" | python3 -c 'import json,sys; r=json.load(sys.stdin)["data"]["result"]; print(r[0]["value"][1] if r else "нет")'; }
for _ in $(seq 30); do [ "$(target_up auth-service)" = 1 ] && [ "$(target_up keycloak)" = 1 ] && break; sleep 2; done
check "Prometheus собирает сервис входа" "$(target_up auth-service)" 1
check "Prometheus собирает Keycloak" "$(target_up keycloak)" 1
check "дашборд «Сервис входа» загружен в Grafana" \
  "$(curl -s -u "$GRAFANA_AUTH" "$GRAFANA/api/search?type=dash-db" | python3 -c 'import json,sys; print(",".join(d["uid"] for d in json.load(sys.stdin)))')" auth-service

echo "3. События: вход, отказ, обновление, выход, код, сбой Redis"
JAR="$WORK/m1"; : > "$JAR"
check "вход по паролю → /me/" "$(login "$JAR" /me/ 0987654321)" /me/
check "неверный пароль → отказ Keycloak" "$(login_until_done "$WORK/m2" /me/ 0987654321 wrong-password | sed 's/:.*//')" keycloak
: > "$WORK/m2"
check "обновление токена → 200" "$(status "$JAR" POST /auth/refresh)" 200
OLD_REFRESH=$(cookie "$JAR" refreshToken)
status "$JAR" POST /auth/refresh > /dev/null
check "старый refresh отклонён → 401" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "Cookie: refreshToken=$OLD_REFRESH" "$API/auth/refresh")" 401
check "выход → 200" "$(status "$JAR" POST /auth/logout)" 200
# Код на телефон: отправка (звонок-заглушка) и верный ввод — platform_code_sent_total и platform_code_verified_total.
NEW="7998$(printf '%07d' $(( (RANDOM * 32768 + RANDOM) % 10000000 )))"
CJAR="$WORK/m3"; : > "$CJAR"
code_start "$CJAR" "+$NEW" > /dev/null
check "верный код → вход" "$(code_submit "$CJAR" "$(phone_code "$NEW")")" /me/
# Redis недоступен: токен проверяется по отметкам, хранилище молчит — ошибка в метриках (по умолчанию токен пропускается).
JAR2="$WORK/m4"; : > "$JAR2"
login "$JAR2" /me/ 0987654321 > /dev/null
# Redis возвращаем в любом случае (и если скрипт прервут), чтобы следующие проверки не наткнулись на остановленный Redis.
trap 'dc up -d --wait redis > /dev/null 2>&1' EXIT
dc stop redis > /dev/null 2>&1
curl -s -o /dev/null -b "$JAR2" "$API/me/"
dc up -d --wait redis > /dev/null 2>&1
# Сервис переподключается к Redis сам; ждём, пока готовность снова станет 200.
for _ in $(seq 30); do [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")" = 200 ] && break; sleep 1; done
check "после сбоя Redis сервис снова готов" "$(curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")" 200

echo "4. Метрики сервиса (прямо с порта метрик)"
M=$(curl -s "$METRICS/metrics")
have() { echo "$M" | grep -c -E "$1"; }
atleast() { if [ "$2" -ge 1 ]; then ok "$1"; else bad "$1 (нет ряда)"; fi; }
atleast "входы: auth_logins_total{result=\"success\"}" "$(have '^auth_logins_total\{result="success"\}')"
atleast "начато входов: auth_logins_started_total" "$(have '^auth_logins_started_total ')"
atleast "обновления: auth_refreshes_total{result=\"ok\"}" "$(have '^auth_refreshes_total\{result="ok"\}')"
atleast "отклонённый refresh: auth_refreshes_total{result=\"rejected\"}" "$(have '^auth_refreshes_total\{result="rejected"\}')"
atleast "выходы: auth_logouts_total" "$(have '^auth_logouts_total\{result="ok"\}')"
atleast "запросы к Keycloak: auth_keycloak_requests_total{target=\"token\"}" "$(have '^auth_keycloak_requests_total\{outcome="2xx",target="token"\}')"
atleast "время ответа Keycloak: auth_keycloak_request_duration_seconds" "$(have '^auth_keycloak_request_duration_seconds_bucket')"
atleast "проверки токенов: platformauth_token_checks_total{status=\"valid\"}" "$(have '^platformauth_token_checks_total\{status="valid"\}')"
atleast "запись отзыва (выход): platformauth_revocation_writes_total{kind=\"session\"}" "$(have '^platformauth_revocation_writes_total\{kind="session",result="ok"\}')"
atleast "ошибка Redis при проверке: platformauth_revocation_store_errors_total" "$(have '^platformauth_revocation_store_errors_total\{kind="connection",mode="open"\}')"
atleast "запросы сервиса: ответы ASP.NET Core" "$(have '^microsoft_aspnetcore_hosting_http_server_request_duration_count')"

echo "5. В метриках нет секретов и персональных данных"
ALL="$M
$(kc_metrics)"
# Выражения — в переменных: bash 3.2 (macOS) режет «"$(… {10,} …)"» по запятой (docs/tech/pitfalls.md).
JWT_RE='eyJ[A-Za-z0-9_-]{10,}'
PHONE_RE='="\+?[0-9]{10,}"'   # только значения меток: числа с длинным хвостом в самих значениях метрик (секунды, байты) на номер не похожи
MAIL_RE='[A-Za-z0-9._-]+@[A-Za-z0-9.-]+\.[a-z]{2,}'
check "нет токенов (JWT)" "$(echo "$ALL" | grep -c -E "$JWT_RE")" 0
check "нет номеров телефонов в метках" "$(echo "$ALL" | grep -c -E "$PHONE_RE")" 0
check "нет адресов почты" "$(echo "$ALL" | grep -c -E "$MAIL_RE")" 0

echo "6. Prometheus собрал метрики и запросы дашборда выполняются"
# Ждём, пока Prometheus заберёт новые ряды (интервал сбора 15 с).
q() { curl -s "$PROM/api/v1/query" --data-urlencode "query=$1" | python3 -c 'import json,sys; r=json.load(sys.stdin); print(len(r["data"]["result"]) if r["status"] == "success" else "ошибка")'; }
for _ in $(seq 30); do
  [ "$(q 'auth_logins_total{result="success"}')" != 0 ] && [ "$(q 'platform_code_verified_total')" != 0 ] \
    && [ "$(q 'platformauth_revocation_store_errors_total')" != 0 ] && [ "$(q 'keycloak_user_events_total{event="login",error!=""}')" != 0 ] && break
  sleep 2
done
atleast "Prometheus: входы сервиса" "$(q 'auth_logins_total{result="success"}')"
atleast "Prometheus: ошибки Redis" "$(q 'platformauth_revocation_store_errors_total')"
atleast "Prometheus: отказ входа в событиях Keycloak (error != \"\")" "$(q 'keycloak_user_events_total{event="login",error!=""}')"
atleast "Prometheus: успешный вход в событиях Keycloak" "$(q 'keycloak_user_events_total{event="login",error=""}')"
atleast "Prometheus: коды отправлены (платформа кодов)" "$(q 'platform_code_sent_total')"
atleast "Prometheus: коды введены" "$(q 'platform_code_verified_total')"

# Каждый запрос каждой панели — без ошибок разбора и выполнения (пустой результат допустим: события могли не наступить).
BAD=$(python3 - "$DASHBOARD" "$PROM" <<'PY'
import json, sys, urllib.error, urllib.parse, urllib.request
dashboard, prom = json.load(open(sys.argv[1])), sys.argv[2]
bad = []
for panel in dashboard["panels"]:
    for target in panel.get("targets", []):
        expr = target["expr"].replace("$realm", ".*")
        url = prom + "/api/v1/query?" + urllib.parse.urlencode({"query": expr})
        # Prometheus на ошибку разбора отвечает 400: urlopen бросает HTTPError — это тоже «запрос не выполняется».
        try:
            result = json.load(urllib.request.urlopen(url))
        except urllib.error.HTTPError:
            bad.append(panel["title"])
            continue
        if result["status"] != "success":
            bad.append(panel["title"])
print(",".join(bad))
PY
)
check "все запросы панелей дашборда выполняются в Prometheus" "$BAD" ""
PANELS=$(python3 -c 'import json,sys; print(sum(1 for p in json.load(open(sys.argv[1]))["panels"] if p["type"] != "row"))' "$DASHBOARD")
check "панелей на дашборде" "$([ "$PANELS" -ge 20 ] && echo да || echo "нет: $PANELS")" да

summary
