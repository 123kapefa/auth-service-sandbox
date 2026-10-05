#!/usr/bin/env bash
# Проверка этапа 10.2 (docs/keycloak/10-phone-code.md): код на телефон — звонком-сбросом (последние 4 цифры номера,
# с которого звонят), SMS — запасной путь. Телефония стенда — заглушка idp-stub (http://localhost:8190/phone).
# Скрипт на время проверки задаёт настройки проекта атрибутами realm (короткая пауза, каналы) и снимает их в конце.
# Запуск: сервис и Keycloak подняты (docker compose), затем
#   scripts/keycloak/check-phone-code.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KEYCLOAK="${KEYCLOAK:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM="${REALM:-sandbox}"

new_phone() { echo "${1:-7997}$(printf '%07d' $(( (RANDOM * 32768 + RANDOM) % 10000000 )))" | cut -c1-11; }

# project <ключ=значение>… — настройки кодов проекта (атрибуты realm platform.codes.*); без аргументов — снять
project() {
  python3 - "$@" > "$WORK/attrs.json" <<'PY'
import json, sys
attrs = {"platform.codes.cooldowns": "", "platform.codes.phone-channels": ""}
for pair in sys.argv[1:]:
    key, value = pair.split("=", 1)
    attrs["platform.codes." + key] = value
print(json.dumps({"attributes": attrs}))
PY
  curl -s -o /dev/null -w '%{http_code}' -X PUT -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
    --data @"$WORK/attrs.json" "$KEYCLOAK/admin/realms/$REALM"
}

# press <jar> <кнопка> — нажать кнопку экрана кода (resend — «Позвонить ещё раз», sendSms — «Прислать SMS»);
# печатает «куда отправили» или ошибку
press() {
  curl -s -o "$WORK/page.html" -b "$1" -c "$1" --data-urlencode "$2=" "$(form_action "$WORK/page.html")"
  local message; message=$(feedback "$WORK/page.html")
  if [ -n "$message" ]; then echo "ошибка: $message"; else code_screen "$WORK/page.html"; fi
}
screen_attr() { kc "$WORK/page.html" "c[\"$1\"]"; }
last_kind() { curl -s "$PHONE_STUB/messages?to=$1" | python3 -c 'import json,sys; m=json.load(sys.stdin); print(m[-1]["kind"] if m else "")'; }

require_service
[ "$(curl -s -o /dev/null -w '%{http_code}' "$PHONE_STUB/messages")" = 200 ] || { echo "Заглушка телефонии не отвечает на $PHONE_STUB"; exit 1; }
TOKEN=$(master_token)
[ -n "$TOKEN" ] || { echo "Нет токена админки Keycloak"; exit 1; }
trap 'project > /dev/null; rm -rf "$WORK"' EXIT

echo "1. Вход звонком: код — последние 4 цифры номера, с которого звонят"
check "настройки проекта приняты (пауза 1 с)" "$(project cooldowns=1)" 204
PHONE=$(new_phone); JAR="$WORK/jar1"; : > "$JAR"
check "экран: «Вам звонят на +$PHONE»" "$(code_start "$JAR" "$PHONE")" "Вам звонят на +$PHONE."
check "код из 4 цифр, есть «Прислать SMS»" "$(screen_attr codeLength) $(screen_attr codeSmsAvailable)" "4 true"
check "заглушка: звонок на номер" "$(last_kind "$PHONE")" call
FROM=$(curl -s "$PHONE_STUB/messages?to=$PHONE" | python3 -c 'import json,sys; print(json.load(sys.stdin)[-1]["from"])')
check "код — последние 4 цифры номера звонящего ($FROM)" "$(phone_code "$PHONE")" "${FROM: -4}"
check "верный код → вход" "$(code_submit "$JAR" "${FROM: -4}")" /me/
check "вошёл новый покупатель +$PHONE" "$(curl -s -b "$JAR" "$API/me/" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["phone"])')" "+$PHONE"
status "$JAR" POST /auth/logout > /dev/null

echo "2. Звонок не дошёл — «Позвонить ещё раз», потом «Прислать SMS»"
PHONE=$(new_phone); JAR="$WORK/jar2"; : > "$JAR"
code_start "$JAR" "$PHONE" > /dev/null
FIRST=$(phone_code "$PHONE")
check "раньше паузы позвонить ещё раз нельзя" "$(starts "$(press "$JAR" resend)" "ошибка: Код уже отправлен")" "ошибка: Код уже отправлен"
sleep 1.1
check "«Позвонить ещё раз» — второй звонок" "$(press "$JAR" resend) $(phone_count "$PHONE" call)" "Вам звонят на +$PHONE. 2"
SECOND=$(phone_code "$PHONE")
if [ "$FIRST" != "$SECOND" ]; then
  check "код первого звонка больше не подходит" "$(code_submit "$JAR" "$FIRST")" "keycloak: Неверный код. Осталось попыток: 4."
fi
sleep 1.1
check "«Прислать SMS» — SMS с кодом из 6 цифр" "$(press "$JAR" sendSms) $(last_kind "$PHONE")" "Отправили код в SMS на +$PHONE. sms"
check "экран SMS: 6 цифр, «Прислать SMS» уже нет" "$(screen_attr codeLength) $(screen_attr codeSmsAvailable)" "6 false"
check "код звонка после SMS не подходит" "$(starts "$(code_submit "$JAR" "$SECOND")" "keycloak: Неверный код")" "keycloak: Неверный код"
SMS=$(phone_code "$PHONE")
check "SMS — 6 цифр" "${#SMS}" 6
sleep 1.1
check "«ещё раз» после SMS — снова SMS" "$(press "$JAR" resend) $(phone_count "$PHONE" sms)" "Отправили код в SMS на +$PHONE. 2"
check "верный код из SMS → вход" "$(code_submit "$JAR" "$(phone_code "$PHONE")")" /me/
status "$JAR" POST /auth/logout > /dev/null

echo "3. Провайдер звонков недоступен — сразу SMS"
PHONE=$(new_phone 7995); JAR="$WORK/jar3"; : > "$JAR"
check "экран SMS (номера 7995… заглушка «не дозванивает»)" "$(code_start "$JAR" "$PHONE")" "Отправили код в SMS на +$PHONE."
check "ушла одна SMS, звонков нет" "$(phone_count "$PHONE" sms) $(phone_count "$PHONE" call)" "1 0"
check "верный код из SMS → вход" "$(code_submit "$JAR" "$(phone_code "$PHONE")")" /me/
status "$JAR" POST /auth/logout > /dev/null

echo "4. Звонки у проекта выключены — только SMS"
project cooldowns=1 phone-channels=sms > /dev/null
PHONE=$(new_phone); JAR="$WORK/jar4"; : > "$JAR"
check "экран SMS" "$(code_start "$JAR" "$PHONE")" "Отправили код в SMS на +$PHONE."
check "звонков не было" "$(phone_count "$PHONE" call)" 0

echo "5. SMS у проекта выключены — только звонки"
project cooldowns=1 phone-channels=call > /dev/null
PHONE=$(new_phone); JAR="$WORK/jar5"; : > "$JAR"
check "экран звонка без «Прислать SMS»" "$(code_start "$JAR" "$PHONE") $(screen_attr codeSmsAvailable)" "Вам звонят на +$PHONE. false"
sleep 1.1
check "«Прислать SMS» в обход экрана — всё равно звонок" "$(press "$JAR" sendSms) $(phone_count "$PHONE" sms)" "Вам звонят на +$PHONE. 0"
PHONE=$(new_phone 7995); JAR="$WORK/jar5b"; : > "$JAR"
check "звонок не прошёл, а SMS нет — «не удалось отправить»" "$(code_start "$JAR" "$PHONE" > /dev/null; feedback "$WORK/page.html")" \
  "Не удалось отправить код. Попробуйте ещё раз позже."

summary
