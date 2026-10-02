#!/usr/bin/env bash
# Проверка этапа 5 (docs/keycloak/05-code-login.md): вход по коду из SMS и из письма через страницу Keycloak.
# Код «SMS» песочницы пишется в лог Keycloak — скрипт берёт его оттуда (нужен docker compose); письма — из Mailpit.
# Запуск: сервис и Keycloak подняты, затем
#   scripts/keycloak/check-code-login.sh                            # Docker: http://localhost:8080
#   API=http://localhost:5080 scripts/keycloak/check-code-login.sh    # Development: dotnet run
# Каждый прогон создаёт в realm новых покупателей: два со случайными номерами 7998XXXXXXX и один с почтой new-*@sandbox.test.
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

MAILPIT="${MAILPIT:-http://localhost:${MAILPIT_UI_PORT:-8025}}"

# code_start <jar> <логин> — форма входа → «Войти по коду»; печатает текст экрана кода или ошибку.
# Страница — в $WORK/page.html.
code_start() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html"
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=/me/"
  curl -s -o "$page" -b "$jar" -c "$jar" "$(header "$h" location)"
  curl -s -o "$page" -b "$jar" -c "$jar" --data-urlencode "username=$2" --data-urlencode "codeLogin=" "$(form_action "$page")"
  code_screen "$page"
}

# code_start_fresh <jar> <логин> — как code_start, но если от прошлого прогона осталась пауза перед повтором (60 с),
# дожидается её и отправляет код заново
code_start_fresh() {
  local screen wait
  screen=$(code_start "$1" "$2")
  if [[ "$(feedback "$WORK/page.html")" == "Код уже отправлен"* ]]; then
    wait=$(feedback "$WORK/page.html" | grep -o 'через [0-9]* с' | head -1 | grep -o '[0-9]*')
    echo "  … пауза перед повтором от прошлого прогона: ждём ${wait} с" >&2
    sleep "$((wait + 1))"
    code_send_again "$1" > /dev/null
  fi
  echo "$screen"
}

# code_screen <html> — «куда отправили» с экрана кода (теми же словами, что страница темы: из codeChannel и
# codeTarget); не экран кода — ошибка со страницы
code_screen() {
  if [ "$(kc_page "$1")" = sandbox-code-login.ftl ]; then
    kc "$1" '("Отправили код письмом на " if c["codeChannel"] == "EMAIL" else "Отправили код в SMS на ") + c["codeTarget"] + "."'
  else
    echo "ошибка: $(feedback "$1" | sed 's/^$/нет экрана кода/')"
  fi
}

# code_send_again <jar> — «Отправить код ещё раз»; печатает сообщение Keycloak
code_send_again() {
  local jar=$1 page="$WORK/page.html"
  curl -s -o "$page" -b "$jar" -c "$jar" --data-urlencode "resend=" "$(form_action "$page")"
  feedback "$page"
}

# code_submit <jar> <код> — ввод кода; печатает, куда сервис вернул браузер, или «keycloak: <ошибка>»
code_submit() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html" url
  curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" --data-urlencode "code=$2" "$(form_action "$page")"
  url=$(header "$h" location)
  [ -n "$url" ] || { echo "keycloak: $(feedback "$page")"; return; }
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$url"
  header "$h" location
}

# sms_code <логин> [после строки N] — последний код из «SMS» (лог Keycloak), ждём до 10 секунд
sms_code() {
  local i code
  for i in 1 2 3 4 5 6 7 8 9 10; do
    code=$(docker compose logs keycloak --since 10m 2>/dev/null | grep "\[SANDBOX SMS\] +$1: код входа" | tail -1 | grep -o '[0-9]\{6\}$')
    [ -n "$code" ] && { echo "$code"; return; }
    sleep 1
  done
}

# sms_count <логин> — сколько «SMS» ушло на номер за 10 минут
sms_count() { docker compose logs keycloak --since 10m 2>/dev/null | grep -c "\[SANDBOX SMS\] +$1: код входа"; }

# mail_code <адрес> — код из последнего письма на адрес (Mailpit), ждём до 10 секунд
mail_code() {
  local i code
  for i in 1 2 3 4 5 6 7 8 9 10; do
    code=$(curl -s "$MAILPIT/api/v1/search?query=to:$1" | python3 -c '
import json,sys,re,urllib.request
messages = json.load(sys.stdin)["messages"]
if messages:
    body = json.load(urllib.request.urlopen(sys.argv[1] + "/api/v1/message/" + messages[0]["ID"]))
    m = re.search(r"\b(\d{6})\b", body["Text"])
    print(m.group(1) if m else "")' "$MAILPIT")
    [ -n "$code" ] && { echo "$code"; return; }
    sleep 1
  done
}

# mail_subject <адрес> — тема последнего письма на адрес
mail_subject() { curl -s "$MAILPIT/api/v1/search?query=to:$1" | python3 -c 'import json,sys; m=json.load(sys.stdin)["messages"]; print(m[0]["Subject"] if m else "")'; }

me() { curl -s -b "$1" "$API/me/" | python3 -c 'import json,sys; r=json.load(sys.stdin)["result"]; v=r[sys.argv[1]]; print(",".join(v) if isinstance(v,list) else v)' "$2" 2>/dev/null; }

require_service
[ "$(curl -s -o /dev/null -w '%{http_code}' "$MAILPIT/api/v1/messages")" = 200 ] || { echo "Mailpit не отвечает на $MAILPIT"; exit 1; }
docker compose ps keycloak --format '{{.State}}' 2>/dev/null | grep -q running || { echo "Keycloak не запущен в docker compose — код из лога не достать"; exit 1; }

echo "1. На форме входа — кнопка «Войти по коду»"
JAR="$WORK/jar"; : > "$JAR"
curl -s -o /dev/null -D "$WORK/h" -b "$JAR" -c "$JAR" "$API/auth/login?return_to=/me/"
# Кнопку рисует тема в браузере (её проверяют тесты login-theme); здесь — что страница входа из нашей темы.
check "страница входа — тема sandbox" \
  "$(curl -s -o "$WORK/page.html" -b "$JAR" -c "$JAR" "$(header "$WORK/h" location)"; kc "$WORK/page.html" 'c["templateName"] + " " + c["url"]["resourcesUrl"].split("/")[-1]')" \
  "login.ftl sandbox"

echo "2. Вход по коду существующего пользователя (номер как набрали, без скрипта темы)"
JAR="$WORK/jar2"; : > "$JAR"
SCREEN=$(code_start_fresh "$JAR" "8 (999) 000-00-03")
check "экран кода: «$SCREEN»" "$SCREEN" "Отправили код в SMS на +79990000003."
check "неверный код отклонён" "$(code_submit "$JAR" 000000)" "keycloak: Неверный код. Осталось попыток: 4."
check "повтор раньше 60 с — не отправляется" "$(starts "$(code_send_again "$JAR")" "Код уже отправлен")" "Код уже отправлен"
check "верный код → вход и возврат на return_to" "$(code_submit "$JAR" "$(sms_code 79990000003)")" /me/
check "вошёл пользователь 79990000003" "$(me "$JAR" phone)" +79990000003
status "$JAR" POST /auth/logout > /dev/null

echo "3. Новый номер — новый покупатель после верного кода"
NEW="7998$(printf '%07d' $((RANDOM * 32768 % 10000000 + RANDOM % 1000)))"
JAR="$WORK/jar3"; : > "$JAR"
check "экран кода для неизвестного номера — такой же" "$(code_start "$JAR" "+$NEW")" "Отправили код в SMS на +$NEW."
check "верный код → вход" "$(code_submit "$JAR" "$(sms_code "$NEW")")" /me/
check "создан покупатель +$NEW" "$(me "$JAR" phone)" "+$NEW"
check "роль customer" "$(me "$JAR" roles)" customer
status "$JAR" POST /auth/logout > /dev/null

echo "4. Пять неверных кодов — блокировка номера"
LOCK="7998$(printf '%07d' $((RANDOM * 32768 % 10000000 + RANDOM % 1000 + 1)))"
JAR="$WORK/jar4"; : > "$JAR"
code_start "$JAR" "$LOCK" > /dev/null
for i in 1 2 3 4; do code_submit "$JAR" 000000 > /dev/null; done
check "пятый неверный — блокировка на 300 с" "$(code_submit "$JAR" 000000)" "keycloak: Слишком много неверных попыток. Попробуйте через 300 с."
check "даже верный код больше не принимается" "$(starts "$(code_submit "$JAR" "$(sms_code "$LOCK")")" "keycloak: Слишком много неверных попыток")" "keycloak: Слишком много неверных попыток"
JAR="$WORK/jar5"; : > "$JAR"
check "новая попытка входа не обходит блокировку" "$(code_start "$JAR" "$LOCK" > /dev/null; starts "$(feedback "$WORK/page.html")" "Слишком много неверных попыток")" "Слишком много неверных попыток"
check "SMS на заблокированный номер не отправлялось повторно" "$(sms_count "$LOCK")" 1

echo "5. Вход по коду из письма"
curl -s -o /dev/null -X DELETE "$MAILPIT/api/v1/messages"
JAR="$WORK/jar8"; : > "$JAR"
SCREEN=$(code_start_fresh "$JAR" " Customer@Sandbox.test ")
check "экран кода: «$SCREEN»" "$SCREEN" "Отправили код письмом на customer@sandbox.test."
check "письмо «Код для входа» пришло" "$(mail_subject customer@sandbox.test)" "Код для входа"
check "верный код из письма → вход" "$(code_submit "$JAR" "$(mail_code customer@sandbox.test)")" /me/
check "вошёл пользователь с этой почтой" "$(me "$JAR" phone)" +79990000001
status "$JAR" POST /auth/logout > /dev/null

NEW_EMAIL="new-$(date +%s)-$RANDOM@sandbox.test"
JAR="$WORK/jar9"; : > "$JAR"
check "неизвестная почта — такой же экран кода" "$(code_start "$JAR" "$NEW_EMAIL")" "Отправили код письмом на $NEW_EMAIL."
check "верный код → вход" "$(code_submit "$JAR" "$(mail_code "$NEW_EMAIL")")" /me/
check "создан покупатель без телефона" "$(me "$JAR" phone)" None
check "роль customer" "$(me "$JAR" roles)" customer
status "$JAR" POST /auth/logout > /dev/null

echo "6. 2FA после кода — только у тех, кто её включил"
# У 79990000006 включена 2FA (OTP). Без 2FA после кода ничего не спрашивают — это разделы 2, 3 и 5.
JAR="$WORK/jar11"; : > "$JAR"
code_start_fresh "$JAR" 79990000006 > /dev/null
code_submit "$JAR" "$(sms_code 79990000006)" > /dev/null
check "после верного кода из SMS спрашивают код 2FA" "$(kc_page "$WORK/page.html")" login-otp.ftl
curl -s -o "$WORK/page.html" -b "$JAR" -c "$JAR" --data-urlencode "otp=000000" "$(form_action "$WORK/page.html")"
check "неверный код 2FA отклонён" "$([ "$(kc_page "$WORK/page.html")" = login-otp.ftl ] && echo "форма осталась: $(feedback "$WORK/page.html")" || echo пустили)" "форма осталась: Неверный код аутентификатора."
curl -s -o "$WORK/page.html" -D "$WORK/h" -b "$JAR" -c "$JAR" --data-urlencode "otp=$(fresh_totp sandbox-otp-test-secret)" \
  "$(form_action "$WORK/page.html")"
URL=$(header "$WORK/h" location)
curl -s -o /dev/null -D "$WORK/h" -b "$JAR" -c "$JAR" "$URL"
check "верный код 2FA → вход" "$(header "$WORK/h" location)" /me/
check "вошёл пользователь с 2FA" "$(me "$JAR" phone)" +79990000006
status "$JAR" POST /auth/logout > /dev/null

echo "7. Отказы"
JAR="$WORK/jar6"; : > "$JAR"
check "ни номер, ни почта — просят телефон или почту" "$(code_start "$JAR" abc)" "ошибка: Для входа по коду введите телефон или почту."
JAR="$WORK/jar7"; : > "$JAR"
code_start_fresh "$JAR" 79990000004 > /dev/null
check "заблокированный пользователь — после кода вход не пускает" \
  "$(starts "$(code_submit "$JAR" "$(sms_code 79990000004)")" "keycloak: Учетная запись заблокирована")" "keycloak: Учетная запись заблокирована"

summary
