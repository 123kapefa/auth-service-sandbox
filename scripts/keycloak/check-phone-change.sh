#!/usr/bin/env bash
# Проверка этапа 10.5 (docs/keycloak/10-phone-change.md): человек сам меняет номер — из кабинета на страницу Keycloak,
# новый номер, код на него (звонок), номер изменён; занятый, тот же и не номер — отказ; не чаще раза в сутки; письмо на
# почту и SMS на старый номер; запись в журнале сервиса; код на старый номер больше не открывает аккаунт.
# Проверка заводит своих пользователей и удаляет их в конце.
# Запуск: стенд поднят (Docker: сервис :8080, Keycloak :8180, Mailpit :8025, заглушка телефонии :8190), затем
#   scripts/keycloak/check-phone-change.sh
# Стенд, поднятый до 10.5: scripts/project/enable-phone-change.py sandbox
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
MAILPIT="${MAILPIT:-http://localhost:${MAILPIT_UI_PORT:-8025}}"
REALM=sandbox
OLD=79990000091           # номер до смены
NEW=79990000092           # новый номер
TAKEN=79990000093         # номер другого аккаунта
EMAIL=phone-change@sandbox.test
ADMIN_LOGIN=1234567890

# токен мастер-админки живёт минуту, а проверка может ждать паузу перед повтором — берём новый на каждый вызов
admin_api() { curl -s -H "Authorization: Bearer $(master_token < /dev/null)" -H 'Content-Type: application/json' "$@"; }
user_id() { admin_api "$KC/admin/realms/$REALM/users?username=$1&exact=true" | python3 -c 'import json,sys; u=json.load(sys.stdin); print(u[0]["id"] if u else "")'; }
remove_user() { local id; id=$(user_id "$1"); [ -z "$id" ] || admin_api -X DELETE "$KC/admin/realms/$REALM/users/$id" > /dev/null; }
remove_all() { remove_user "$OLD"; remove_user "$NEW"; remove_user "$TAKEN"; }
# create_user <логин> [почта] — покупатель с паролем; номер — логин и атрибут phone
create_user() {
  python3 -c 'import json,sys
u = {"username": sys.argv[1], "enabled": True, "attributes": {"phone": ["+" + sys.argv[1]], "active_mode": ["customer"]},
     "credentials": [{"type": "password", "value": sys.argv[3], "temporary": False}]}
if sys.argv[2]:
    u.update(email=sys.argv[2], emailVerified=True)
print(json.dumps(u))' "$1" "${2:-}" "$PASSWORD" | admin_api -o /dev/null -w '%{http_code}' -d @- "$KC/admin/realms/$REALM/users"
}
# user_attr <id> <поле> — логин или атрибут пользователя в Keycloak («—» — нет)
user_attr() {
  admin_api "$KC/admin/realms/$REALM/users/$1" | python3 -c '
import json,sys
u = json.load(sys.stdin)
print(u["username"] if sys.argv[1] == "username" else ((u.get("attributes") or {}).get(sys.argv[1]) or ["—"])[0])' "$2"
}
trap 'remove_all; rm -rf "$WORK"' EXIT

PAGE() { echo "$WORK/page.html"; }
# start <jar> — «Сменить номер» из кабинета; печатает шаг страницы (phone, code) или куда вернули
start() {
  local jar=$1 h="$WORK/h"
  local url="$API/auth/login?return_to=/account/&action=update-phone"
  # сервис → Keycloak (kc_action) → страница действия: идём по переадресациям, пока они есть
  while [ -n "$url" ]; do
    curl -s -o "$(PAGE)" -D "$h" -b "$jar" -c "$jar" "$url"
    url=$(header "$h" location)
  done
  step
}
# step — шаг страницы смены номера или её имя (страница входа — вход заново)
step() { kc "$(PAGE)" 'get("phoneStep") or get("templateName") or ""'; }
# post <jar> <поле=значение>… — отправить форму страницы; печатает шаг, а если ушли со страницы — куда вернул сервис
post() {
  local jar=$1 action args=() url
  shift
  action=$(form_action "$(PAGE)")
  for kv in "$@"; do args+=(--data-urlencode "$kv"); done
  curl -s -o "$(PAGE)" -D "$WORK/h" -b "$jar" -c "$jar" "${args[@]}" "$action"
  url=$(header "$WORK/h" location)
  if [ -n "$url" ]; then
    curl -s -o /dev/null -D "$WORK/h" -b "$jar" -c "$jar" "$url"
    header "$WORK/h" location
  else
    step
  fi
}
error() { feedback "$(PAGE)"; }
# send_phone <jar> <номер> — новый номер; если от прошлого прогона осталась пауза перед повтором — дождаться и отправить
send_phone() {
  local screen wait
  screen=$(post "$1" "phone=$2")
  if [[ "$(error)" == "Код уже отправлен"* ]]; then
    wait=$(error | grep -o 'через [0-9]* с' | head -1 | grep -o '[0-9]*')
    echo "  … пауза перед повтором от прошлого прогона: ждём ${wait} с" >&2
    sleep "$((wait + 1))"
    screen=$(post "$1" "resend=")
  fi
  echo "$screen"
}
# last_text <номер> — текст последней SMS на номер (заглушка телефонии)
last_text() { curl -s "$PHONE_STUB/messages?to=$1" | python3 -c 'import json,sys; m=[x for x in json.load(sys.stdin) if x.get("text")]; print(m[-1]["text"] if m else "")'; }
# mail_text — первая строка последнего письма «Номер телефона изменён» (ждём до 10 секунд)
mail_text() {
  local i text
  for i in 1 2 3 4 5 6 7 8 9 10; do
    text=$(curl -s "$MAILPIT/api/v1/search?query=to:$EMAIL" | python3 -c '
import json,sys,urllib.request
m = [x for x in json.load(sys.stdin)["messages"] if x["Subject"] == "Номер телефона изменён"]
if m:
    body = json.load(urllib.request.urlopen(sys.argv[1] + "/api/v1/message/" + m[0]["ID"]))
    print(body["Text"].strip().splitlines()[0])' "$MAILPIT")
    [ -n "$text" ] && { echo "$text"; return; }
    sleep 1
  done
  echo "письма нет"
}
me() { curl -s -b "$1" "$API/me/" | python3 -c 'import json,sys; r=json.load(sys.stdin).get("result") or {}; print(r.get("id", ""), r.get("phone", ""))'; }

require_service
WORK=$(mktemp -d)
remove_all
curl -s -o /dev/null -X DELETE "$MAILPIT/api/v1/search?query=to:$EMAIL"
check "пользователь создан (номер, почта, пароль)" "$(create_user "$OLD" "$EMAIL")" 201
check "другой аккаунт с номером, который будет занят" "$(create_user "$TAKEN")" 201
ID=$(user_id "$OLD")
J="$WORK/jar"; : > "$J"
check "вход по паролю" "$(login "$J" /account/ "$OLD")" /account/

echo "1. Новый номер: проверки"
check "«Сменить номер» из кабинета — страница нового номера (вошли только что — без входа заново)" "$(start "$J")" phone
check "нынешний номер на странице" "$(kc "$(PAGE)" 'get("phoneCurrent")')" "+$OLD"
check "не номер — «Введите номер телефона…»" "$(post "$J" "phone=abc" > /dev/null; error)" "Введите номер телефона, например +7 999 000-00-01."
check "тот же номер — «Это ваш нынешний номер.»" "$(post "$J" "phone=8 999 000 00 91" > /dev/null; error)" "Это ваш нынешний номер."
check "номер другого аккаунта — занят, написать в поддержку" "$(post "$J" "phone=+$TAKEN" > /dev/null; error)" \
  "Этот номер уже привязан к другому аккаунту. Если это ваш номер, напишите в поддержку."

echo "2. Код на новый номер"
CALLS=$(phone_count "$NEW" call)
check "свободный номер → экран кода" "$(send_phone "$J" "+7 (999) 000-00-92")" code
check "звонок на новый номер" "$(kc "$(PAGE)" '"Вам звонят на " + c["codeTarget"] + "." if c.get("codeDelivery") == "CALL" else c.get("codeDelivery")')" "Вам звонят на +$NEW."
check "звонок ушёл (заглушка телефонии)" "$(phone_count "$NEW" call)" "$((CALLS + 1))"
check "неверный код — ошибка, номер прежний" "$(post "$J" "code=0000" > /dev/null; starts "$(error)" "Неверный код")" "Неверный код"
check "«Другой номер» — снова страница номера" "$(post "$J" "changePhone=")" phone
check "код входа на новый номер для смены не годится (свои коды)" "$(send_phone "$J" "+$NEW" > /dev/null; post "$J" "code=$(phone_code "$NEW" | tr 0-9 1-90)" > /dev/null; starts "$(error)" "Неверный код")" "Неверный код"
check "верный код → назад в кабинет, «готово»" "$(post "$J" "code=$(phone_code "$NEW")")" "/account/?action_status=success"

echo "3. Номер изменён"
check "логин — новый номер" "$(user_attr "$ID" username)" "$NEW"
check "атрибут phone — новый номер" "$(user_attr "$ID" phone)" "+$NEW"
check "прежний номер записан" "$(user_attr "$ID" phone_previous)" "+$OLD"
check "письмо на почту" "$(mail_text)" "В Auth Sandbox у вашего аккаунта новый номер телефона: +7 *** ***-**-92. Код на старый номер больше не открывает аккаунт."
check "SMS на старый номер" "$(last_text "$OLD")" \
  "В Auth Sandbox к аккаунту с этим номером привязан другой номер (+7 *** ***-**-92): по этому номеру в него больше не войти. Если это сделали не вы, сразу обратитесь в поддержку."
ADMIN="$WORK/admin"; : > "$ADMIN"
login "$ADMIN" /me/ "$ADMIN_LOGIN" > /dev/null
check "в журнале сервиса — смена номера, сделал сам человек" \
  "$(curl -s -b "$ADMIN" "$API/auth/admin/audit?objectId=$ID&action=UserPhoneChanged&pageSize=10" | python3 -c '
import json,sys
items = json.load(sys.stdin)["result"]["items"]
print(len(items), items[0]["actorId"] == items[0]["objectId"], items[0]["changes"]["phone"]["old"], "→", items[0]["changes"]["phone"]["new"])')" \
  "1 True +$OLD → +$NEW"

echo "4. Вход после смены"
NEWJAR="$WORK/new"; : > "$NEWJAR"
code_start_fresh "$NEWJAR" "$NEW" > /dev/null
check "код на новый номер → вход в этот аккаунт" "$(code_submit "$NEWJAR" "$(phone_code "$NEW")" > /dev/null; me "$NEWJAR" | cut -d' ' -f1)" "$ID"
OLDJAR="$WORK/old"; : > "$OLDJAR"
code_start_fresh "$OLDJAR" "$OLD" > /dev/null
code_submit "$OLDJAR" "$(phone_code "$OLD")" > /dev/null
OTHER=$(me "$OLDJAR")
check "код на старый номер — не в этот аккаунт (новый покупатель со старым номером)" \
  "$([ "${OTHER%% *}" != "$ID" ] && echo "${OTHER#* }")" "+$OLD"
check "пароль по новому номеру → вход" "$(: > "$WORK/p"; login "$WORK/p" /me/ "$NEW")" /me/

echo "5. Не чаще раза в сутки"
check "снова «Сменить номер» — сразу отказ" "$(start "$J" > /dev/null; starts "$(error)" "Номер можно менять раз в сутки")" "Номер можно менять раз в сутки"
check "поля нового номера нет (когда можно — на странице)" "$([ -n "$(kc "$(PAGE)" 'get("phoneNextAllowed") or ""')" ] && echo есть)" есть
check "«Отмена» — назад в кабинет, «отменено»" "$(post "$J" "cancel-aia=true")" "/account/?action_status=cancelled"

summary
