#!/usr/bin/env bash
# Проверка этапа 10.7 (docs/keycloak/10-code-settings.md): коды подтверждения проекта в API админки — каналы, страны,
# бюджет, лимиты и расход за сегодня. Хранятся в атрибутах realm (platform.codes.*), меняются через сервис входа и
# плагин platform-codes, плагин применяет их со следующей отправки. В конце настройки проекта снимаются (как на стенде).
# Запуск: стенд поднят (Docker: сервис :8080, Keycloak :8180), затем
#   scripts/keycloak/check-code-settings.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM=sandbox
PLUGIN="$KC/realms/$REALM/platform-codes"
PHONE=79990000003
BFF_SECRET="${SANDBOX_BFF_CLIENT_SECRET:-$(grep -s '^SANDBOX_BFF_CLIENT_SECRET=' "$(dirname "$0")/../../.env" | cut -d= -f2)}"
KEYS="phone-channels countries daily-budget target-per-hour target-per-day ip-per-hour range-per-hour captcha-after"

# Вход по коду на один номер дважды: между отправками — пауза стенда (60 с), её скрипт дожидается.
# Все запросы к Keycloak — «с нового IP»: прошлые проверки не упрутся в капчу (KC_PROXY_HEADERS стенда).
IP="10.$((RANDOM % 250)).$((RANDOM % 250)).$((RANDOM % 250 + 1))"
curl() { command curl -H "X-Forwarded-For: $IP" "$@"; }

MASTER=$(master_token)
# api <jar> <метод> <путь> [тело] — печатает «<код> <код ошибки или result>»
api() {
  curl -s -o "$WORK/r.json" -w '%{http_code}' -b "$1" -X "$2" -H 'Content-Type: application/json' ${4:+-d "$4"} "$API$3"
  printf ' '
  python3 -c '
import json,sys
try: d=json.load(open(sys.argv[1]))
except Exception: print("—"); sys.exit()
print(d["error"]["messages"][0]["code"] if d.get("error") else json.dumps(d["result"], ensure_ascii=False, sort_keys=True))' "$WORK/r.json"
}
# result <jar> <путь> <python-выражение над r> — значение из result
result() { curl -s -b "$1" "$API$2" | python3 -c "import json,sys; r=json.load(sys.stdin)['result']; print($3)"; }
user_token() {
  curl -s -d grant_type=password -d client_id=dev-cli -d "username=$1" -d "password=$PASSWORD" \
    "$KC/realms/$REALM/protocol/openid-connect/token" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])'
}
# attrs — настройки проекта в realm (platform.codes.*), заданные непусто
attrs() {
  curl -s -H "Authorization: Bearer $(master_token)" "$KC/admin/realms/$REALM" | python3 -c '
import json,sys; a=json.load(sys.stdin).get("attributes",{})
print(" ".join(f"{k[15:]}={v}" for k,v in sorted(a.items()) if k.startswith("platform.codes.") and v))'
}
# unset_attrs — снять настройки проекта (пустой атрибут — «не задано»): стенд снова по compose.yaml
unset_attrs() {
  python3 -c 'import json,sys; print(json.dumps({"attributes": {"platform.codes." + k: "" for k in sys.argv[1].split()}}))' "$KEYS" > "$WORK/attrs.json"
  command curl -s -o /dev/null -X PUT -H "Authorization: Bearer $(master_token)" -H 'Content-Type: application/json' \
    --data @"$WORK/attrs.json" "$KC/admin/realms/$REALM"
}
# code_login <номер> — войти по коду; печатает, куда вернули
code_login() {
  local jar="$WORK/code"; : > "$jar"
  code_start_fresh "$jar" "$1" > /dev/null
  code_submit "$jar" "$(phone_code "$1")"
}

require_service
ADMIN="$WORK/admin"; : > "$ADMIN"
MANAGER="$WORK/manager"; : > "$MANAGER"
login "$ADMIN" /admin/ 1234567890 > /dev/null
login "$MANAGER" /admin/ 0987654321 > /dev/null
trap 'unset_attrs; rm -rf "$WORK"' EXIT
unset_attrs

echo "1. Плагин Keycloak: читать и менять — только учётке с ролью manage-sign-in"
check "без токена — нет" "$(curl -s -o /dev/null -w '%{http_code}' "$PLUGIN/settings") $(curl -s -o /dev/null -w '%{http_code}' "$PLUGIN/usage")" "401 401"
check "токеном пользователя, даже админа проекта, — нет" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X PUT -H "Authorization: Bearer $(user_token 71234567890)" -H 'Content-Type: application/json' -d '{}' "$PLUGIN/settings")" 403
SA_ID=$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/users?username=service-account-sandbox-bff&exact=true" \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])')
check "у служебной учётки сервиса — роль manage-sign-in" \
  "$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/users/$SA_ID/role-mappings/realm" \
    | python3 -c 'import json,sys; print("manage-sign-in" in [x["name"] for x in json.load(sys.stdin)])')" True

echo "2. Админка: коды — только админ проекта"
check "в «кто я» у админа — право sign-in.manage" \
  "$(result "$ADMIN" /auth/admin/me '"sign-in.manage" in r["permissions"]')" True
check "менеджеру — нет" "$(api "$MANAGER" GET /auth/admin/codes/settings | cut -d' ' -f1) $(api "$MANAGER" GET /auth/admin/codes/usage | cut -d' ' -f1)" "403 403"
check "настройки — как на стенде (compose.yaml) и по умолчанию" \
  "$(result "$ADMIN" /auth/admin/codes/settings 'r["phoneChannels"], r["countries"], r["dailyBudget"], r["targetPerHour"], r["captchaAfter"], r["smsCost"], r["callCost"], r["zone"]')" \
  "['call', 'sms'] ['7'] 0 1000 1000 400 100 Europe/Moscow"

echo "3. Расход за сегодня: отправленные и введённые коды"
BEFORE=$(result "$ADMIN" /auth/admin/codes/usage 'r["call"]["sent"], r["call"]["cost"], r["verified"], r["budget"]["spent"]')
read -r CALLS COST VERIFIED SPENT <<< "$BEFORE"
check "вход по коду (звонок)" "$(code_login $PHONE)" /me/
check "звонок посчитан с ценой, код — введённым" \
  "$(result "$ADMIN" /auth/admin/codes/usage 'r["call"]["sent"], r["call"]["cost"], r["verified"], r["budget"]["spent"]')" \
  "$((CALLS + 1)) $((COST + 100)) $((VERIFIED + 1)) $((SPENT + 100))"
check "доля введённых и сутки по Москве" \
  "$(result "$ADMIN" /auth/admin/codes/usage '0 < r["verifiedPercent"] <= 100, r["date"], r["zone"]')" \
  "True $(TZ=Europe/Moscow date +%F) Europe/Moscow"

echo "4. Сохранить: только SMS, ещё страна, бюджет, лимиты"
check "сохранено" \
  "$(api "$ADMIN" PUT /auth/admin/codes/settings '{"phoneChannels":["sms"],"countries":["7","375"],"dailyBudget":300000,"targetPerHour":50,"targetPerDay":500,"ipPerHour":500,"rangePerHour":900,"captchaAfter":100}' \
    | python3 -c 'import sys,json; c,r=sys.stdin.read().split(" ",1); r=json.loads(r); print(c, r["phoneChannels"], r["countries"], r["dailyBudget"], r["captchaAfter"])')" \
  "200 ['sms'] ['7', '375'] 300000 100"
check "в realm — настройки проекта" "$(attrs)" \
  "captcha-after=100 countries=7,375 daily-budget=300000 ip-per-hour=500 phone-channels=sms range-per-hour=900 target-per-day=500 target-per-hour=50"
read -r SMS CALLS <<< "$(result "$ADMIN" /auth/admin/codes/usage 'r["sms"]["sent"], r["call"]["sent"]')"
check "вход по коду — сразу SMS, без звонка" "$(code_login $PHONE)" /me/
check "SMS посчитана, звонков не прибавилось, бюджет — новый" \
  "$(result "$ADMIN" /auth/admin/codes/usage 'r["sms"]["sent"], r["call"]["sent"], r["budget"]["limit"]')" "$((SMS + 1)) $CALLS 300000"
check "в журнале — что поменялось" \
  "$(result "$ADMIN" '/auth/admin/audit?pageSize=1' '
(lambda i, c: (i["action"], sorted(c), c["phoneChannels"]["new"]))(r["items"][0], json.loads(r["items"][0]["changes"]) if isinstance(r["items"][0]["changes"], str) else r["items"][0]["changes"])')" \
  "('ProjectCodesChanged', ['captchaAfter', 'countries', 'dailyBudget', 'ipPerHour', 'phoneChannels', 'rangePerHour', 'targetPerDay', 'targetPerHour'], ['sms'])"

echo "5. Неверное — не сохраняется"
BODY='"countries":["7"],"dailyBudget":0,"targetPerHour":5,"targetPerDay":10,"ipPerHour":10,"rangePerHour":20,"captchaAfter":2'
check "без звонков и SMS" "$(api "$ADMIN" PUT /auth/admin/codes/settings "{\"phoneChannels\":[],$BODY}")" "400 value.is.required"
check "страна с «+»" "$(api "$ADMIN" PUT /auth/admin/codes/settings "{\"phoneChannels\":[\"sms\"],${BODY/\[\"7\"\]/[\"+7\"]}}")" "400 invalid.format"
check "в сутки меньше, чем в час" \
  "$(api "$ADMIN" PUT /auth/admin/codes/settings '{"phoneChannels":["sms"],"countries":["7"],"dailyBudget":0,"targetPerHour":5,"targetPerDay":3,"ipPerHour":10,"rangePerHour":20,"captchaAfter":2}')" \
  "400 codes.day_below_hour"
check "плагин тоже не принимает неверное (мимо сервиса)" \
  "$(command curl -s -X PUT -H "Authorization: Bearer $(curl -s -d grant_type=client_credentials -d client_id=sandbox-bff -d client_secret="$BFF_SECRET" "$KC/realms/$REALM/protocol/openid-connect/token" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')" \
    -H 'Content-Type: application/json' -d '{"phoneChannels":["sms"],"countries":["7"],"dailyBudget":0,"targetPerHour":5,"targetPerDay":10,"ipPerHour":10,"rangePerHour":20,"captchaAfter":-1}' "$PLUGIN/settings")" \
  '{"error":"Капча после — от 0 до 100."}'
check "настройки не изменились" "$(attrs | tr ' ' '\n' | grep phone-channels)" "phone-channels=sms"

summary
