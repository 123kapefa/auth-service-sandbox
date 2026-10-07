#!/usr/bin/env bash
# Проверка этапа 10.4 (docs/keycloak/10-two-step-login.md): вход в два шага — сначала телефон или почта, потом чем
# войти. Шаг 2 с паролем (и для незнакомого логина) — пароль, код на то, что ввели, ключ доступа; шаг 2 без пароля
# (человек выключил его) — включённые способы по цене; один способ — открывается сам. Ответы для существующего и
# незнакомого логина не отличаются. Проверка заводит своих пользователей и удаляет их в конце.
# Запуск: стенд поднят (Docker: сервис :8080, Keycloak :8180, Mailpit :8025, заглушка телефонии :8190), затем
#   scripts/keycloak/check-two-step-login.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
MAILPIT="${MAILPIT:-http://localhost:${MAILPIT_UI_PORT:-8025}}"
REALM=sandbox
WITH=79990000097          # пароль включён
WITH_EMAIL=two-step-with@sandbox.test
WITHOUT=79990000098       # пароль выключен
WITHOUT_EMAIL=two-step-without@sandbox.test
UNKNOWN=79990000099       # аккаунта нет (код создаёт его — удаляется в конце)
UNKNOWN_EMAIL=two-step-nobody@sandbox.test
OTP_SECRET=two-step-otp-secret

MASTER=$(master_token)
admin_api() { curl -s -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' "$@"; }
user_id() { admin_api "$KC/admin/realms/$REALM/users?username=$1&exact=true" | python3 -c 'import json,sys; u=json.load(sys.stdin); print(u[0]["id"] if u else "")'; }
remove_user() { local id; id=$(user_id "$1"); [ -z "$id" ] || admin_api -X DELETE "$KC/admin/realms/$REALM/users/$id" > /dev/null; }
remove_all() { remove_user "$WITH"; remove_user "$WITHOUT"; remove_user "$UNKNOWN"; remove_user "$UNKNOWN_EMAIL"; }
# disable <логин> <способы через запятую> — атрибут disabled_sign_in_methods (в жизни его пишет сервис по правилам 10.3)
disable() {
  local id; id=$(user_id "$1")
  admin_api "$KC/admin/realms/$REALM/users/$id" | python3 -c '
import json,sys
u = json.load(sys.stdin)
u.setdefault("attributes", {})["disabled_sign_in_methods"] = [sys.argv[1]]
print(json.dumps(u))' "$2" | admin_api -o /dev/null -X PUT -d @- "$KC/admin/realms/$REALM/users/$id"
}
# drop_credential <логин> <тип> — удалить учётные данные этого типа
drop_credential() {
  local id cred; id=$(user_id "$1")
  for cred in $(admin_api "$KC/admin/realms/$REALM/users/$id/credentials" | python3 -c 'import json,sys; print(" ".join(c["id"] for c in json.load(sys.stdin) if c["type"] == sys.argv[1]))' "$2"); do
    admin_api -X DELETE "$KC/admin/realms/$REALM/users/$id/credentials/$cred" > /dev/null
  done
}

# create_user <логин> <почта> <с аутентификатором: yes|no> — пароль, телефон, почта, ключ доступа (запись без
# настоящего устройства: входить им проверка не будет) и, если попросили, приложение-аутентификатор.
create_user() {
  local json
  json=$(python3 -c 'import json,sys
cred = {"aaguid": "00000000-0000-0000-0000-000000000000", "credentialId": "dHdvLXN0ZXA", "counter": 0,
        "credentialPublicKey": "pQECAyYgASFYIA", "attestationStatementFormat": "none", "transports": []}
creds = [{"type": "password", "value": sys.argv[3], "temporary": False},
         {"type": "webauthn-passwordless", "userLabel": "Ключ проверки", "secretData": "{}", "credentialData": json.dumps(cred)}]
if sys.argv[4] == "yes":
    creds.append({"type": "otp", "userLabel": "Аутентификатор проверки", "secretData": json.dumps({"value": sys.argv[5]}),
                  "credentialData": json.dumps({"subType": "totp", "digits": 6, "counter": 0, "period": 30, "algorithm": "HmacSHA1"})})
print(json.dumps({"username": sys.argv[1], "enabled": True, "email": sys.argv[2], "emailVerified": True,
  "attributes": {"phone": ["+" + sys.argv[1]]}, "credentials": creds}))' "$1" "$2" "$PASSWORD" "$3" "$OTP_SECRET")
  admin_api -o /dev/null -w '%{http_code}' -d "$json" "$KC/admin/realms/$REALM/users"
}
trap 'remove_all; rm -rf "$WORK"' EXIT

# PAGE — текущая страница входа
PAGE() { echo "$WORK/page.html"; }
# open_login <jar> — страница входа (шаг 1); печатает loginStep
open_login() {
  local jar=$1 h="$WORK/h"
  : > "$jar"
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=/me/"
  curl -s -o "$(PAGE)" -b "$jar" -c "$jar" "$(header "$h" location)"
  kc "$(PAGE)" 'get("loginStep") or ""'
}
# post <jar> <поле=значение>… — отправить форму текущей страницы; редирект — в $WORK/location
post() {
  local jar=$1 action args=()
  shift
  action=$(form_action "$(PAGE)")
  for kv in "$@"; do args+=(--data-urlencode "$kv"); done
  curl -s -o "$(PAGE)" -D "$WORK/h" -b "$jar" -c "$jar" "${args[@]}" "$action"
  header "$WORK/h" location > "$WORK/location"
}
# step1 <jar> <логин> — шаг 1: логин → «Продолжить»
step1() { open_login "$1" > /dev/null; post "$1" "username=$2" "next="; }
# view — что на шаге 2: «<шаг> <способы> [почта] [номер] [сам]»
view() {
  kc "$(PAGE)" '" ".join(x for x in [get("loginStep") or get("templateName"), get("loginMethods") or "-", get("loginEmailHint"), get("loginPhoneHint"), "сам:" + get("loginAutoStart") if get("loginAutoStart") else None] if x)'
}
# shape <логин> — отпечаток страницы без того, что меняется от запроса к запросу (коды сессии в адресах, вызов ключа
# доступа) и без самого логина: у существующего и незнакомого должно совпасть всё остальное
shape() {
  python3 - "$(PAGE)" "$1" "$(dirname "$0")" <<'EOF'
import hashlib, json, re, sys
sys.path.insert(0, sys.argv[3])
import kc_context
c = kc_context.load(sys.argv[1]) or {}
for key in ("url", "challenge", "createTimeout", "authenticationSession"):
    c.pop(key, None)
text = json.dumps(c, ensure_ascii=False, sort_keys=True)
for login in (sys.argv[2], "+" + sys.argv[2]):
    text = text.replace(login, "LOGIN")
text = re.sub(r"(tab_id|client_data|session_code|execution)=[^&\"]*", r"\1=", text)
print(hashlib.sha256(text.encode()).hexdigest()[:16])
EOF
}
# finish <jar> — пройти редирект в сервис (предложение ключа доступа после кода — «Не сейчас»); печатает, куда вернул
# сервис
finish() {
  local url
  skip_passkey_offer "$1" "$(PAGE)" "$WORK/h"
  header "$WORK/h" location > "$WORK/location"
  url=$(cat "$WORK/location")
  [ -n "$url" ] || { echo "keycloak: $(feedback "$(PAGE)" | sed 's/^$/осталась форма/') ($(kc_page "$(PAGE)"))"; return; }
  curl -s -o /dev/null -D "$WORK/h" -b "$1" -c "$1" "$url"
  header "$WORK/h" location
}
# code_in <jar> <код> — ввести код на экране кода
code_in() { post "$1" "code=$2"; finish "$1"; }
# mail_code <почта> — код из последнего письма (ждём до 10 секунд)
mail_code() {
  local i code
  for i in 1 2 3 4 5 6 7 8 9 10; do
    code=$(curl -s "$MAILPIT/api/v1/search?query=to:$1" | python3 -c '
import json,sys,re,urllib.request
messages = [m for m in json.load(sys.stdin)["messages"] if m["Subject"] == "Код для входа"]
if messages:
    body = json.load(urllib.request.urlopen(sys.argv[1] + "/api/v1/message/" + messages[0]["ID"]))
    m = re.search(r"\b(\d{6})\b", body["Text"])
    print(m.group(1) if m else "")' "$MAILPIT")
    [ -n "$code" ] && { echo "$code"; return; }
    sleep 1
  done
}
clear_mail() { curl -s -o /dev/null -X DELETE "$MAILPIT/api/v1/search?query=to:$1"; }

require_service
WORK=$(mktemp -d)
remove_all
for address in "$WITH_EMAIL" "$WITHOUT_EMAIL" "$UNKNOWN_EMAIL"; do clear_mail "$address"; done
check "пользователь с паролем создан" "$(create_user "$WITH" "$WITH_EMAIL" no)" 201
check "пользователь без пароля создан (с аутентификатором)" "$(create_user "$WITHOUT" "$WITHOUT_EMAIL" yes)" 201
disable "$WITHOUT" password
J="$WORK/jar"; U="$WORK/unknown"

echo "1. Шаг 1 — только телефон или почта"
check "страница входа — шаг «логин»" "$(open_login "$J")" login
check "пустой логин — «Введите телефон или почту.»" "$(post "$J" "username=" "next="; feedback "$(PAGE)")" "Введите телефон или почту."
check "«Изменить» на шаге 2 — снова шаг 1" "$(step1 "$J" "$WITH"; post "$J" "changeLogin="; kc "$(PAGE)" 'get("loginStep")')" login

echo "2. Шаг 2 с паролем: существующий и незнакомый — одинаково"
step1 "$J" "$WITH"
check "по номеру: пароль сразу, ключ доступа, звонок" "$(view)" "password passkey,phone-code"
check "номер — как ввели, с «+»" "$(kc "$(PAGE)" 'get("loginDisplay")')" "+$WITH"
KNOWN_SHAPE=$(shape "$WITH")
step1 "$U" "$UNKNOWN"
check "незнакомый номер — тот же вид" "$(view)" "password passkey,phone-code"
check "страница незнакомого номера не отличается ничем, кроме номера" "$(shape "$UNKNOWN")" "$KNOWN_SHAPE"
step1 "$J" "$WITH_EMAIL"
check "по почте: пароль сразу, ключ доступа, код на почту" "$(view)" "password passkey,email-code"
KNOWN_SHAPE=$(shape "$WITH_EMAIL")
step1 "$U" "$UNKNOWN_EMAIL"
check "незнакомая почта — та же страница" "$(shape "$UNKNOWN_EMAIL")" "$KNOWN_SHAPE"
disable "$WITH" phone-code
step1 "$J" "$WITH"
check "выключенный код на телефон при включённом пароле не виден" "$(view)" "password passkey,phone-code"
disable "$WITH" ""

echo "3. Шаг 2 с паролем: пароль, отказ, коды"
step1 "$J" "$WITH"; post "$J" "password=$PASSWORD"
check "верный пароль → вход" "$(finish "$J")" /me/
step1 "$J" "$WITH"; post "$J" "password=Wrong-1234!"; KNOWN=$(feedback "$(PAGE)")
step1 "$U" "$UNKNOWN"; post "$U" "password=Wrong-1234!"
check "неверный пароль и незнакомый номер — один ответ" "$(feedback "$(PAGE)")" "$KNOWN"
admin_api -X DELETE "$KC/admin/realms/$REALM/attack-detection/brute-force/users/$(user_id "$WITH")" > /dev/null
step1 "$J" "$WITH"; post "$J" "codeLogin=phone-code"
check "«Войти по коду» → звонок на номер, как ввели" "$(code_screen "$(PAGE)")" "Вам звонят на +$WITH."
check "код звонка → вход" "$(code_in "$J" "$(phone_code "$WITH")")" /me/
step1 "$U" "$UNKNOWN"; post "$U" "codeLogin=phone-code"
check "незнакомый номер — тот же экран звонка" "$(code_screen "$(PAGE)")" "Вам звонят на +$UNKNOWN."
check "код → вход, аккаунт создан (как раньше)" "$(code_in "$U" "$(phone_code "$UNKNOWN")")" /me/
step1 "$J" "$WITH_EMAIL"; post "$J" "codeLogin=email-code"
check "по почте — код письмом на почту, как ввели" "$(code_screen "$(PAGE)")" "Отправили код письмом на $WITH_EMAIL."
check "код из письма → вход" "$(code_in "$J" "$(mail_code "$WITH_EMAIL")")" /me/
step1 "$J" "$WITH"; post "$J" "codeLogin=email-code"
check "код на почту, когда ввели номер, — нет" "$(feedback "$(PAGE)")" "Этот способ входа недоступен — выберите другой."

echo "4. Шаг 2 без пароля: включённые способы по цене"
step1 "$J" "$WITHOUT"
check "ключ доступа → код на почту → звонок, почта и номер скрыты" "$(view)" \
  "methods passkey,email-code,phone-code t***@sandbox.test +7 *** ***-**-98"
step1 "$J" "$WITHOUT_EMAIL"
check "по почте — тот же список" "$(view)" "methods passkey,email-code,phone-code t***@sandbox.test +7 *** ***-**-98"
post "$J" "codeLogin=phone-code"
check "звонок на номер из аккаунта (скрыт)" "$(code_screen "$(PAGE)")" "Вам звонят на +7 *** ***-**-98."
post "$J" "code=$(phone_code "$WITHOUT")"
check "код → второй шаг: приложение-аутентификатор" "$(kc_page "$(PAGE)")" login-otp.ftl
post "$J" "otp=$(fresh_totp "$OTP_SECRET")"
check "код аутентификатора → вход" "$(finish "$J")" /me/
step1 "$J" "$WITHOUT"; post "$J" "codeLogin=email-code"
check "код на почту из аккаунта (скрыта)" "$(code_screen "$(PAGE)")" "Отправили код письмом на t***@sandbox.test."
check "письмо с кодом пришло" "$([ -n "$(mail_code "$WITHOUT_EMAIL")" ] && echo да)" да
step1 "$J" "$WITHOUT"; post "$J" "password=$PASSWORD"
check "пароль — отказ, как на неверный (10.3)" "$(feedback "$(PAGE)")" "$KNOWN"
admin_api -X DELETE "$KC/admin/realms/$REALM/attack-detection/brute-force/users/$(user_id "$WITHOUT")" > /dev/null

echo "5. Один способ — открывается сам"
disable "$WITHOUT" "password,phone-code"
step1 "$J" "$WITHOUT"
check "выключен звонок — его нет в списке" "$(view)" "methods passkey,email-code t***@sandbox.test"
post "$J" "codeLogin=phone-code"
check "выбрать выключенный звонок — нельзя" "$(feedback "$(PAGE)")" "Этот способ входа недоступен — выберите другой."
disable "$WITHOUT" "password,phone-code,email-code"
step1 "$J" "$WITHOUT"
check "остался ключ доступа — страница открывает его сама" "$(view)" "methods passkey сам:passkey"
drop_credential "$WITHOUT" webauthn-passwordless
disable "$WITHOUT" "password,phone-code"
step1 "$J" "$WITHOUT"
check "остался код на почту — шаг 2 пропущен, сразу экран кода" "$(code_screen "$(PAGE)")" "Отправили код письмом на t***@sandbox.test."
# Новый код — только после паузы; действует код из письма выше.
post "$J" "code=$(mail_code "$WITHOUT_EMAIL")"
check "код из письма → приложение-аутентификатор" "$(kc_page "$(PAGE)")" login-otp.ftl

echo "6. Запрос в один шаг (менеджеры паролей, старые скрипты)"
open_login "$J" > /dev/null; post "$J" "username=$WITH" "password=$PASSWORD"
check "логин и пароль сразу → вход" "$(finish "$J")" /me/

summary
