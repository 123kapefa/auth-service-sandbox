#!/usr/bin/env bash
# Проверка этапа 10.1 (docs/keycloak/10-code-limits.md): защита от накрутки кодов — растущая пауза, капча, лимиты
# по номеру, IP и диапазону, страны, дневной бюджет, метрики.
# На стенде лимиты ослаблены (compose.yaml); скрипт на время проверки задаёт свои атрибутами realm
# (platform.codes.*, как настройки проекта) и снимает их в конце. IP — свой у каждого раздела (X-Forwarded-For).
# Запуск: сервис и Keycloak подняты (docker compose), затем
#   scripts/keycloak/check-code-limits.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KEYCLOAK="${KEYCLOAK:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM="${REALM:-sandbox}"
MAILPIT="${MAILPIT:-http://localhost:${MAILPIT_UI_PORT:-8025}}"
KEYS="cooldowns target-per-hour target-per-day ip-per-hour range-per-hour range-digits captcha-after countries daily-budget sms-cost call-cost phone-channels budget-alert-percent zone"

# Все запросы — «с IP» из $IP: Keycloak стенда берёт адрес из X-Forwarded-For (KC_PROXY_HEADERS).
IP=""
curl() { if [ -n "$IP" ]; then command curl -H "X-Forwarded-For: $IP" "$@"; else command curl "$@"; fi; }
new_ip() { echo "10.$((RANDOM % 250)).$((RANDOM % 250)).$((RANDOM % 250 + 1))"; }
new_phone() { echo "${1:-7997}$(printf '%07d' $(( (RANDOM * 32768 + RANDOM) % 10000000 )))" | cut -c1-11; }

# limits <ключ=значение>… — настройки защиты на время раздела; остальные ключи сняты (стенд: compose.yaml)
limits() {
  local args=("$@")
  python3 - "$KEYS" "${args[@]}" > "$WORK/attrs.json" <<'PY'
import json, sys
attrs = {"platform.codes." + key: "" for key in sys.argv[1].split()}
for pair in sys.argv[2:]:
    key, value = pair.split("=", 1)
    attrs["platform.codes." + key] = value
print(json.dumps({"attributes": attrs}))
PY
  command curl -s -o /dev/null -w '%{http_code}' -X PUT -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
    --data @"$WORK/attrs.json" "$KEYCLOAK/admin/realms/$REALM"
}

# send_again <jar> [токен капчи] — «Отправить код ещё раз»; печатает сообщение Keycloak
send_again() {
  local args=(--data-urlencode "resend=")
  [ -n "${2:-}" ] && args+=(--data-urlencode "smart-token=$2")
  curl -s -o "$WORK/page.html" -b "$1" -c "$1" "${args[@]}" "$(form_action "$WORK/page.html")"
  feedback "$WORK/page.html"
}

# send_sms <jar> — «Прислать SMS»; печатает сообщение Keycloak
send_sms() {
  curl -s -o "$WORK/page.html" -b "$1" -c "$1" --data-urlencode "sendSms=" "$(form_action "$WORK/page.html")"
  feedback "$WORK/page.html"
}
resend_after() { kc "$WORK/page.html" 'c["codeResendAfter"]'; }
captcha_flag() { kc "$WORK/page.html" 'c.get("captchaRequired", "")'; }
metric() {
  docker compose exec -T keycloak bash -c 'exec 3<>/dev/tcp/127.0.0.1/9000 && printf "GET /metrics HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3 && cat <&3' 2>/dev/null |
    grep "^$1" | awk '{ s += $2 } END { printf "%d\n", s }'
}
LIMIT_TEXT="Слишком много запросов кода"

require_service
docker compose ps keycloak --format '{{.State}}' 2>/dev/null | grep -q running || { echo "Keycloak не запущен в docker compose"; exit 1; }
TOKEN=$(master_token)
[ -n "$TOKEN" ] || { echo "Нет токена админки Keycloak"; exit 1; }
trap 'IP=""; limits > /dev/null; rm -rf "$WORK"' EXIT
REFUSED_BEFORE=$(metric 'platform_code_refused_total')

echo "1. Пауза перед повтором растёт с каждой отправкой (на время проверки: 2 → 4 → 6 с)"
check "настройки проекта приняты" "$(limits cooldowns=2,4,6 captcha-after=100)" 204
IP=$(new_ip); PHONE=$(new_phone); JAR="$WORK/jar1"; : > "$JAR"
code_start "$JAR" "$PHONE" > /dev/null
check "1-я отправка — пауза 2 с" "$(resend_after)" 2
sleep 2.2
send_again "$JAR" > /dev/null
check "2-я отправка — пауза 4 с" "$(resend_after)" 4
check "раньше паузы — не отправляется" "$(starts "$(send_again "$JAR")" "Код уже отправлен")" "Код уже отправлен"
sleep 4.2
send_again "$JAR" > /dev/null
check "3-я отправка — пауза 6 с" "$(resend_after)" 6
check "ушло 3 кода" "$(phone_count "$PHONE")" 3

echo "2. Капча после 2 отправок за час"
limits cooldowns=1 captcha-after=2 > /dev/null
IP=$(new_ip); PHONE=$(new_phone); JAR="$WORK/jar2"; : > "$JAR"
code_start "$JAR" "$PHONE" > /dev/null
check "1-я отправка — без капчи" "$(captcha_flag)" false
sleep 1.1
send_again "$JAR" > /dev/null
check "после 2-й экран просит капчу для следующей" "$(captcha_flag)" true
sleep 1.1
check "3-я без капчи — отказ" "$(send_again "$JAR")" "Подтвердите, что вы не робот, и запросите код ещё раз."
check "3-я с неверной капчей — отказ" "$(send_again "$JAR" wrong-token)" "Подтвердите, что вы не робот, и запросите код ещё раз."
check "без капчи код не ушёл" "$(phone_count "$PHONE")" 2
check "3-я с капчей — отправлена" "$(send_again "$JAR" sandbox-captcha-ok)" ""
check "ушло 3 кода" "$(phone_count "$PHONE")" 3
OTHER=$(new_phone); JAR="$WORK/jar2b"; : > "$JAR"
code_start "$JAR" "$OTHER" > /dev/null
check "с того же IP на другой номер — тоже капча" "$(feedback "$WORK/page.html")" "Подтвердите, что вы не робот, и запросите код ещё раз."
check "на другой номер код не ушёл" "$(phone_count "$OTHER")" 0

echo "3. Лимит на номер (на время проверки: 3 в час) — отказ даже с капчей"
limits cooldowns=1 captcha-after=2 target-per-hour=3 > /dev/null
IP=$(new_ip); PHONE=$(new_phone); JAR="$WORK/jar3"; : > "$JAR"
code_start "$JAR" "$PHONE" > /dev/null
sleep 1.1; send_again "$JAR" > /dev/null
sleep 1.1; send_again "$JAR" sandbox-captcha-ok > /dev/null
check "3 кода ушли" "$(phone_count "$PHONE")" 3
sleep 1.1
check "4-я с капчей — лимит" "$(starts "$(send_again "$JAR" sandbox-captcha-ok)" "$LIMIT_TEXT")" "$LIMIT_TEXT"
check "4-я без капчи — лимит" "$(starts "$(send_again "$JAR")" "$LIMIT_TEXT")" "$LIMIT_TEXT"
IP=$(new_ip); JAR="$WORK/jar3b"; : > "$JAR"
code_start "$JAR" "$PHONE" > /dev/null
check "новый вход с другого IP не обходит лимит номера" "$(starts "$(feedback "$WORK/page.html")" "$LIMIT_TEXT")" "$LIMIT_TEXT"
check "больше 3 кодов не ушло" "$(phone_count "$PHONE")" 3

echo "4. Лимит с IP (на время проверки: 3 в час)"
limits cooldowns=1 captcha-after=100 ip-per-hour=3 > /dev/null
IP=$(new_ip)
for i in 1 2 3; do JAR="$WORK/jar4$i"; : > "$JAR"; code_start "$JAR" "$(new_phone)" > /dev/null; done
check "3 номера с одного IP — коды ушли" "$(feedback "$WORK/page.html")" ""
PHONE=$(new_phone); JAR="$WORK/jar44"; : > "$JAR"
code_start "$JAR" "$PHONE" > /dev/null
check "4-й номер с того же IP — лимит" "$(starts "$(feedback "$WORK/page.html")" "$LIMIT_TEXT")" "$LIMIT_TEXT"
check "код не ушёл" "$(phone_count "$PHONE")" 0
IP=$(new_ip); JAR="$WORK/jar45"; : > "$JAR"
code_start "$JAR" "$PHONE" > /dev/null
check "тот же номер с другого IP — код ушёл" "$(phone_count "$PHONE")" 1

echo "5. Лимит на диапазон номеров (на время проверки: 2 в час на первые 7 цифр)"
limits cooldowns=1 captcha-after=100 range-per-hour=2 > /dev/null
RANGE="7996$(printf '%03d' $((RANDOM % 1000)))"
for i in 1 2; do IP=$(new_ip); JAR="$WORK/jar5$i"; : > "$JAR"; code_start "$JAR" "$(new_phone "$RANGE")" > /dev/null; done
check "2 номера диапазона +$RANGE — коды ушли" "$(feedback "$WORK/page.html")" ""
IP=$(new_ip); PHONE=$(new_phone "$RANGE"); JAR="$WORK/jar53"; : > "$JAR"
code_start "$JAR" "$PHONE" > /dev/null
check "3-й номер диапазона с нового IP — лимит" "$(starts "$(feedback "$WORK/page.html")" "$LIMIT_TEXT")" "$LIMIT_TEXT"
check "код не ушёл" "$(phone_count "$PHONE")" 0

echo "6. Только разрешённые страны (+7)"
limits cooldowns=1 captcha-after=100 > /dev/null
IP=$(new_ip); JAR="$WORK/jar6"; : > "$JAR"
code_start "$JAR" "+375 29 $(printf '%07d' $((RANDOM * 30)))" > /dev/null
check "номер +375 — отказ" "$(feedback "$WORK/page.html")" "На номера этой страны коды не отправляем. Войдите по почте."
check "на +375 не звонили и SMS не слали" "$(curl -s "$PHONE_STUB/messages" | grep -c '"to": "375')" 0

echo "7. Дневной бюджет исчерпан — SMS не уходят, звонки и почта работают"
limits cooldowns=1 captcha-after=100 daily-budget=1 > /dev/null
IP=$(new_ip); PHONE=$(new_phone); JAR="$WORK/jar7"; : > "$JAR"
check "звонок идёт и без бюджета" "$(code_start "$JAR" "$PHONE")" "Вам звонят на +$PHONE."
sleep 1.1
check "«Прислать SMS» — отказ" "$(send_sms "$JAR")" "Сейчас не можем отправить SMS. Войдите по почте или с ключом доступа."
check "SMS не ушла" "$(phone_count "$PHONE" sms)" 0
check "в логе Keycloak — оповещение о бюджете" \
  "$(docker compose logs keycloak --since 24h 2>/dev/null | grep -c "\[CODE BUDGET\] $REALM: дневной бюджет [0-9]* ₽ исчерпан" | sed 's/^[1-9][0-9]*$/есть/')" есть
EMAIL="limits-$(date +%s)-$RANDOM@sandbox.test"; JAR="$WORK/jar7b"; : > "$JAR"
check "код на почту уходит" "$(code_start "$JAR" "$EMAIL")" "Отправили код письмом на $EMAIL."
sleep 2
CODE=$(command curl -s "$MAILPIT/api/v1/search?query=to:$EMAIL" | python3 -c '
import json,sys,re,urllib.request
m = json.load(sys.stdin)["messages"]
body = json.load(urllib.request.urlopen(sys.argv[1] + "/api/v1/message/" + m[0]["ID"])) if m else {"Text": ""}
r = re.search(r"\b(\d{6})\b", body["Text"])
print(r.group(1) if r else "")' "$MAILPIT")
check "код из письма → вход" "$(code_submit "$JAR" "$CODE")" /me/

echo "8. Метрики Prometheus"
check "отправки по каналам" "$(metric 'platform_code_sent_total{channel="CALL"' | sed 's/^[1-9][0-9]*$/есть/')" есть
check "расход" "$(metric 'platform_code_cost_kopecks_total' | sed 's/^[1-9][0-9]*$/есть/')" есть
check "введённые коды" "$(metric 'platform_code_verified_total{channel="EMAIL"' | sed 's/^[1-9][0-9]*$/есть/')" есть
check "отказы этого прогона: капча, лимиты, страна, бюджет — не меньше 9" \
  "$([ $(( $(metric 'platform_code_refused_total') - REFUSED_BEFORE )) -ge 9 ] && echo да)" да
check "оповещения о бюджете" "$(metric 'platform_code_budget_alerts_total{percent="100"' | sed 's/^[1-9][0-9]*$/есть/')" есть

summary
