#!/usr/bin/env bash
# Проверка этапа 5 (docs/keycloak/05-code-login.md): вход по коду из SMS через страницу Keycloak.
# Код «SMS» песочницы пишется в лог Keycloak — скрипт берёт его оттуда (нужен docker compose).
# Запуск: сервис и Keycloak подняты, затем
#   scripts/keycloak/check-code-login.sh                            # Docker: http://localhost:8080
#   API=http://localhost:5080 scripts/keycloak/check-code-login.sh    # Development: dotnet run
# Каждый прогон создаёт в realm двух новых покупателей со случайными номерами 7998XXXXXXX.
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

# code_start <jar> <логин> — форма входа → «Войти по коду»; печатает текст экрана кода или ошибку.
# Страница — в $WORK/page.html.
code_start() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html"
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=/me/"
  curl -s -o "$page" -b "$jar" -c "$jar" "$(header "$h" location)"
  curl -s -o "$page" -b "$jar" -c "$jar" --data-urlencode "username=$2" --data-urlencode "codeLogin=" \
    "$(form_action "$page" kc-form-login)"
  code_screen "$page"
}

# code_screen <html> — «куда отправили» с экрана кода; иначе — ошибка со страницы
code_screen() {
  python3 -c '
import re,sys,html
s = open(sys.argv[1]).read()
m = re.search(r"<p id=\"sandbox-code-target\">([^<]+)", s)
e = re.search(r"kc-feedback-text[^>]*>\s*([^<]+)", s) or re.search(r"id=\"input-error-username\"[^>]*>\s*([^<]+)", s)
print(html.unescape(m.group(1)).strip() if m else "ошибка: " + (html.unescape(e.group(1)).strip() if e else "нет экрана кода"))' "$1"
}

# code_send_again <jar> — «Отправить код ещё раз»; печатает сообщение Keycloak
code_send_again() {
  local jar=$1 page="$WORK/page.html"
  curl -s -o "$page" -b "$jar" -c "$jar" --data-urlencode "resend=" "$(form_action "$page" sandbox-code-form)"
  feedback "$page"
}

# code_submit <jar> <код> — ввод кода; печатает, куда сервис вернул браузер, или «keycloak: <ошибка>»
code_submit() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html" url
  curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" --data-urlencode "code=$2" "$(form_action "$page" sandbox-code-form)"
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

# starts <текст> <начало> — печатает <начало>, если текст с него начинается, иначе весь текст (cut на macOS режет байты, не буквы)
starts() { if [[ "$1" == "$2"* ]]; then echo "$2"; else echo "$1"; fi; }

me() { curl -s -b "$1" "$API/me/" | python3 -c 'import json,sys; r=json.load(sys.stdin)["result"]; v=r[sys.argv[1]]; print(",".join(v) if isinstance(v,list) else v)' "$2" 2>/dev/null; }

require_service
docker compose ps keycloak --format '{{.State}}' 2>/dev/null | grep -q running || { echo "Keycloak не запущен в docker compose — код из лога не достать"; exit 1; }

echo "1. На форме входа — кнопка «Войти по коду»"
JAR="$WORK/jar"; : > "$JAR"
curl -s -o /dev/null -D "$WORK/h" -b "$JAR" -c "$JAR" "$API/auth/login?return_to=/me/"
check "кнопка на странице входа" "$(curl -s -b "$JAR" -c "$JAR" "$(header "$WORK/h" location)" | grep -q 'name="codeLogin"' && echo да || echo нет)" да

echo "2. Вход по коду существующего пользователя (номер как набрали, без скрипта темы)"
# Прошлый прогон мог оставить паузу перед повтором (60 с) — тогда ждём её.
JAR="$WORK/jar2"; : > "$JAR"
SCREEN=$(code_start "$JAR" "8 (999) 000-00-03")
WAIT=$(grep -o 'через [0-9]* с' "$WORK/page.html" | head -1 | grep -o '[0-9]*')
if grep -q 'Код уже отправлен' "$WORK/page.html"; then
  echo "  … пауза перед повтором от прошлого прогона: ждём ${WAIT} с"; sleep "$((WAIT + 1))"; code_send_again "$JAR" > /dev/null
fi
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

echo "5. Отказы"
JAR="$WORK/jar6"; : > "$JAR"
check "почта вместо номера — просят номер" "$(code_start "$JAR" customer@sandbox.test)" "ошибка: Для входа по коду введите номер телефона."
JAR="$WORK/jar7"; : > "$JAR"
code_start "$JAR" 79990000004 > /dev/null
check "заблокированный пользователь — после кода вход не пускает" \
  "$(starts "$(code_submit "$JAR" "$(sms_code 79990000004)")" "keycloak: Учетная запись заблокирована")" "keycloak: Учетная запись заблокирована"

summary
