#!/usr/bin/env bash
# Проверка писем админам о бюджете кодов (docs/keycloak/10-code-limits.md, «Оповещения о расходе»): расход дошёл до
# порога (80 %) — письмо админам проекта, бюджет исчерпан — второе письмо; каждое — не больше раза в сутки на проект,
# покупателям и менеджерам — ничего. Письма — в почтовой заглушке стенда (Mailpit).
# Бюджет на время проверки — «расход за сегодня + 1,20 ₽»: один звонок (1 ₽) переводит за 80 %, SMS (4 ₽) — за 100 %.
# Отметка «уже оповестили» живёт сутки (по Москве): повторный прогон в те же сутки новых писем не ждёт и проверяет, что
# их по-прежнему по одному. Если 100 % в эти сутки уже было до первого прогона (например, check-code-limits.sh), письма
# о 80 % не будет: скрипт так и скажет — перезапустите Keycloak, он сбрасывает расход.
# Запуск: стенд поднят (Docker: сервис :8080, Keycloak :8180, заглушка телефонии :8190, Mailpit :8025), затем
#   scripts/keycloak/check-budget-alerts.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KEYCLOAK="${KEYCLOAK:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM="${REALM:-sandbox}"
MAILPIT="${MAILPIT:-http://localhost:${MAILPIT_UI_PORT:-8025}}"
BFF_SECRET="${SANDBOX_BFF_CLIENT_SECRET:-$(grep -s '^SANDBOX_BFF_CLIENT_SECRET=' "$(dirname "$0")/../../.env" | cut -d= -f2)}"
ADMINS="admin@sandbox.test customer-admin@sandbox.test"     # роль admin в sandbox-api (право sign-in.manage)
OTHERS="customer@sandbox.test manager@sandbox.test"
SUBJECT_80="Коды: израсходовано 80 % дневного бюджета"
SUBJECT_100="Коды: дневной бюджет исчерпан"

IP="10.$((RANDOM % 250)).$((RANDOM % 250)).$((RANDOM % 250 + 1))"
curl() { command curl -H "X-Forwarded-For: $IP" "$@"; }
new_phone() { echo "7997$(printf '%07d' $(( (RANDOM * 32768 + RANDOM) % 10000000 )))"; }

# setting <ключ=значение>… — настройки кодов realm (platform.codes.*); пустое значение — снять
setting() {
  python3 -c '
import json, sys
print(json.dumps({"attributes": {"platform.codes." + k: v for k, v in (a.split("=", 1) for a in sys.argv[1:])}}))' "$@" > "$WORK/attrs.json"
  put_realm_attributes "$KEYCLOAK/admin/realms/$REALM" "$(master_token)" "$WORK/attrs.json"
}
# spent — расход за сегодня, копейки (плагин, служебная учётка сервиса с правом manage-sign-in)
spent() {
  local token
  token=$(curl -s -d grant_type=client_credentials -d client_id=sandbox-bff -d client_secret="$BFF_SECRET" \
    "$KEYCLOAK/realms/$REALM/protocol/openid-connect/token" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')
  curl -s -H "Authorization: Bearer $token" "$KEYCLOAK/realms/$REALM/platform-codes/usage" |
    python3 -c 'import json,sys; print(json.load(sys.stdin)["budget"]["spent"])'
}
# Отметка «уже оповестили» — в памяти Keycloak: перезапуск её сбрасывает. Поэтому письма считаются с начала суток
# (по Москве) или со старта Keycloak, что позже: за это время их ровно по одному.
SINCE=$(docker inspect -f '{{.State.StartedAt}}' "$(docker compose ps -q keycloak)" 2>/dev/null)
# mails <адрес> <тема> — сколько писем с такой темой пришло на адрес с SINCE. Поиск Mailpit «to:» — по подстроке
# (admin@ находит и customer-admin@), поэтому адрес сверяется точно
mails() {
  command curl -s "$MAILPIT/api/v1/search?limit=500&query=$(python3 -c 'import sys,urllib.parse; print(urllib.parse.quote(sys.argv[1]))' "to:$1")" |
    python3 -c '
import json, re, sys
from datetime import datetime, timezone
from zoneinfo import ZoneInfo
def parse(value):
    value = re.sub(r"(\.\d{6})\d*", r"\1", value.replace("Z", "+00:00"))
    return datetime.fromisoformat(value)
since = datetime.now(ZoneInfo("Europe/Moscow")).replace(hour=0, minute=0, second=0, microsecond=0)
if sys.argv[3]:
    since = max(since, parse(sys.argv[3]))
print(sum(1 for m in json.load(sys.stdin)["messages"]
          if m["Subject"] == sys.argv[1] and sys.argv[2] in [t["Address"] for t in m["To"]] and parse(m["Created"]) >= since))' "$2" "$1" "$SINCE"
}
# text <адрес> <тема> — текст последнего такого письма
text() {
  local id
  id=$(command curl -s "$MAILPIT/api/v1/search?query=$(python3 -c 'import sys,urllib.parse; print(urllib.parse.quote(sys.argv[1]))' "to:$1")" |
    python3 -c 'import json,sys; m=[m for m in json.load(sys.stdin)["messages"] if m["Subject"] == sys.argv[1] and sys.argv[2] in [t["Address"] for t in m["To"]]]; print(m[0]["ID"] if m else "")' "$2" "$1")
  [ -n "$id" ] && command curl -s "$MAILPIT/api/v1/message/$id" | python3 -c 'import json,sys; print(json.load(sys.stdin)["Text"])'
}
# send_sms <jar> — «Прислать SMS» на экране кода; печатает сообщение Keycloak
send_sms() {
  curl -s -o "$WORK/page.html" -b "$1" -c "$1" --data-urlencode "sendSms=" "$(form_action "$WORK/page.html")"
  feedback "$WORK/page.html"
}
cleanup() { setting daily-budget= cooldowns= captcha-after= > /dev/null; }
trap 'cleanup; rm -rf "$WORK"' EXIT

require_service
if [ "$(mails admin@sandbox.test "$SUBJECT_80")" = 0 ] && [ "$(mails admin@sandbox.test "$SUBJECT_100")" != 0 ]; then
  echo "Сегодня бюджет уже был исчерпан до этой проверки — письма о 80 % в эти сутки не будет."
  echo "Перезапустите Keycloak (docker compose restart keycloak) и запустите проверку снова."
  exit 1
fi
SPENT=$(spent)
BUDGET=$((SPENT + 120))
echo "Расход за сегодня: $SPENT коп.; бюджет на время проверки: $BUDGET коп."
check "бюджет задан" "$(setting daily-budget=$BUDGET cooldowns=1 captcha-after=100)" 204

echo "1. Расход дошёл до 80 % — письмо админам"
PHONE=$(new_phone); JAR="$WORK/jar1"; : > "$JAR"
check "звонок (1 ₽) уходит" "$(code_start "$JAR" "$PHONE")" "Вам звонят на +$PHONE."
sleep 2
for to in $ADMINS; do check "$to — одно письмо о 80 % за сутки" "$(mails "$to" "$SUBJECT_80")" 1; done
check "в письме — проект и что будет при 100 %" \
  "$(text admin@sandbox.test "$SUBJECT_80" | grep -c -e 'дневного бюджета' -e 'SMS с кодами перестанут уходить до полуночи')" 2

echo "2. Бюджет исчерпан — SMS не уходит, второе письмо админам"
sleep 1.1
check "«Прислать SMS» — отказ" "$(send_sms "$JAR")" \
  "Сейчас не можем отправить SMS. Войдите по почте или с ключом доступа."
sleep 2
for to in $ADMINS; do check "$to — одно письмо об исчерпанном бюджете за сутки" "$(mails "$to" "$SUBJECT_100")" 1; done
check "в письме — SMS выключены до полуночи" \
  "$(text admin@sandbox.test "$SUBJECT_100" | grep -c 'SMS с кодами выключены до полуночи')" 1

echo "3. Повторно в те же сутки — писем больше нет"
IP="10.$((RANDOM % 250)).$((RANDOM % 250)).$((RANDOM % 250 + 1))"
PHONE=$(new_phone); JAR="$WORK/jar3"; : > "$JAR"
check "ещё звонок уходит (звонки бюджет не выключает)" "$(code_start "$JAR" "$PHONE")" "Вам звонят на +$PHONE."
sleep 1.1
send_sms "$JAR" > /dev/null
sleep 2
for to in $ADMINS; do
  check "$to — по-прежнему по одному письму" "$(mails "$to" "$SUBJECT_80") $(mails "$to" "$SUBJECT_100")" "1 1"
done

echo "4. Не админам — ничего"
for to in $OTHERS; do
  check "$to — писем о бюджете нет" "$(mails "$to" "$SUBJECT_80") $(mails "$to" "$SUBJECT_100")" "0 0"
done

summary
