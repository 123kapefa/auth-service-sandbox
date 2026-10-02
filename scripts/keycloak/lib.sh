# Общие помощники скриптов проверки входа (check-login.sh, check-login-methods.sh, check-code-login.sh, check-migration.sh).
# Подключается через source; ведёт себя как браузер: curl + файл cookie (jar).
# shellcheck shell=bash

API="${API:-http://localhost:8080}"
PASSWORD='Test1234!'
PASS=0; FAIL=0
WORK=$(mktemp -d); trap 'rm -rf "$WORK"' EXIT

# shellcheck source=master-token.sh
source "$(dirname "${BASH_SOURCE[0]}")/master-token.sh"

ok()    { PASS=$((PASS+1)); printf '  \033[32m✓\033[0m %s\n' "$1"; }
bad()   { FAIL=$((FAIL+1)); printf '  \033[31m✗\033[0m %s\n' "$1"; }
check() { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1 (ожидали '$3', получили '$2')"; fi; }

# starts <текст> <начало> — печатает <начало>, если текст с него начинается, иначе весь текст (cut на macOS режет байты, не буквы)
starts() { if [[ "$1" == "$2"* ]]; then echo "$2"; else echo "$1"; fi; }

# header <файл заголовков> <имя> — значение заголовка ответа
header() { grep -i "^$2:" "$1" | tail -1 | cut -d' ' -f2- | tr -d '\r'; }
# cookie <jar> <имя> — значение cookie из jar curl
cookie() { awk -v n="$2" '$6 == n { v = $7 } END { print v }' "$1"; }
# status <jar> <метод> <путь> — HTTP-код запроса к сервису с cookie из jar
status() { curl -s -o /dev/null -w '%{http_code}' -b "$1" -c "$1" -X "$2" "$API$3"; }

# login <jar> <return_to> <логин> [пароль] — весь вход; печатает, куда сервис вернул браузер (или ошибку шага)
login() {
  local jar=$1 h="$WORK/h" url
  url=$(login_until_done "$jar" "$2" "$3" "${4:-$PASSWORD}") || { echo "$url"; return; }
  # Callback сервиса: меняет код на токены, ставит cookie и отправляет на return_to.
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$url"
  header "$h" location
}

# login_until_done <jar> <return_to> <логин> <пароль> — форма входа Keycloak (телефон или почта + пароль).
# Успех (код 0): печатает адрес callback. Иначе (код 1): печатает ошибку; страница, на которой остановились, — в $WORK/page.html
# (например, форма кода 2FA).
login_until_done() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html" url action
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=$2"
  url=$(header "$h" location)
  [ -n "$url" ] || { echo "нет редиректа на вход"; return 1; }

  # Страница входа. Если в Keycloak уже есть сессия (SSO), он сразу вернёт в callback без формы.
  curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" "$url"
  url=$(header "$h" location)
  [ -z "$url" ] || { echo "$url"; return 0; }

  [ "$(kc_page "$page")" = login.ftl ] || { echo "нет формы входа"; return 1; }
  action=$(form_action "$page")
  curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" --data-urlencode "username=$3" --data-urlencode "password=$4" "$action"
  url=$(header "$h" location)
  # Не вошли — сообщение Keycloak из kcContext страницы.
  [ -n "$url" ] || { echo "keycloak: $(feedback "$page" | sed 's/^$/осталась форма/')"; return 1; }
  echo "$url"
}

# totp <секрет> — текущий 6-значный код (RFC 6238: HMAC-SHA1, шаг 30 с), как у приложения-аутентификатора
totp() {
  python3 -c '
import hmac,hashlib,struct,sys,time
digest = hmac.new(sys.argv[1].encode(), struct.pack(">Q", int(time.time()) // 30), hashlib.sha1).digest()
offset = digest[-1] & 15
print("%06d" % ((struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7fffffff) % 1000000))' "$1"
}

# fresh_totp <секрет> — код из следующего 30-секундного окна (ждёт его начала): Keycloak не принимает
# один и тот же код дважды, а текущий мог уже использовать другой скрипт проверки
fresh_totp() {
  sleep "$((30 - $(date +%s) % 30 + 1))"
  totp "$1"
}

# Страницы входа рисует тема Keycloakify (React) в браузере: в HTML форм нет, всё нужное — в kcContext страницы.
# kc <html> <python-выражение над c> — значение из kcContext (нет контекста — пусто); c["templateName"] — страница.
kc() { python3 "$(dirname "${BASH_SOURCE[0]}")/kc_context.py" "$1" "$2"; }

# kc_page <html> — какая страница Keycloak: login.ftl, login-otp.ftl, sandbox-code-login.ftl… (пусто — не страница входа)
kc_page() { kc "$1" 'c["templateName"]'; }

# form_action <html> — адрес отправки формы страницы входа (у всех форм страницы — url.loginAction)
form_action() { kc "$1" 'c["url"]["loginAction"]'; }

# feedback <html> — сообщение Keycloak на странице (ошибка поля, предупреждение, страница ошибки) — message.summary
feedback() { kc "$1" 'get("message", "summary") or ""'; }

# Вход по коду (плагин code-login) — check-code-login.sh, check-migration.sh.
# code_start <jar> <логин> — форма входа → «Войти по коду»; печатает текст экрана кода или ошибку.
# Страница — в $WORK/page.html.
code_start() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html"
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=/me/"
  curl -s -o "$page" -b "$jar" -c "$jar" "$(header "$h" location)"
  curl -s -o "$page" -b "$jar" -c "$jar" --data-urlencode "username=$2" --data-urlencode "codeLogin=" "$(form_action "$page")"
  code_screen "$page"
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

# code_submit <jar> <код> — ввод кода; печатает, куда сервис вернул браузер, или «keycloak: <ошибка>»
code_submit() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html" url
  curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" --data-urlencode "code=$2" "$(form_action "$page")"
  url=$(header "$h" location)
  [ -n "$url" ] || { echo "keycloak: $(feedback "$page")"; return; }
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$url"
  header "$h" location
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

# code_send_again <jar> — «Отправить код ещё раз»; печатает сообщение Keycloak
code_send_again() {
  local jar=$1 page="$WORK/page.html"
  curl -s -o "$page" -b "$jar" -c "$jar" --data-urlencode "resend=" "$(form_action "$page")"
  feedback "$page"
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

# require_service — сервис должен отвечать, иначе проверять нечего
require_service() {
  echo "Сервис: $API"
  [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/health/live")" = 200 ] || { echo "Сервис не отвечает на $API/health/live"; exit 1; }
}

# summary — итог и код выхода
summary() {
  echo
  echo "Итого: $PASS ✓, $FAIL ✗"
  [ "$FAIL" -eq 0 ]
}
