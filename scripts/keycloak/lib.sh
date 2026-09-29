# Общие помощники скриптов проверки входа (check-login.sh, check-login-methods.sh).
# Подключается через source; ведёт себя как браузер: curl + файл cookie (jar).
# shellcheck shell=bash

API="${API:-http://localhost:8080}"
PASSWORD='Test1234!'
PASS=0; FAIL=0
WORK=$(mktemp -d); trap 'rm -rf "$WORK"' EXIT

ok()    { PASS=$((PASS+1)); printf '  \033[32m✓\033[0m %s\n' "$1"; }
bad()   { FAIL=$((FAIL+1)); printf '  \033[31m✗\033[0m %s\n' "$1"; }
check() { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1 (ожидали '$3', получили '$2')"; fi; }

# header <файл заголовков> <имя> — значение заголовка ответа
header() { grep -i "^$2:" "$1" | tail -1 | cut -d' ' -f2- | tr -d '\r'; }
# cookie <jar> <имя> — значение cookie из jar curl
cookie() { awk -v n="$2" '$6 == n { v = $7 } END { print v }' "$1"; }
# status <jar> <метод> <путь> — HTTP-код запроса к сервису с cookie из jar
status() { curl -s -o /dev/null -w '%{http_code}' -b "$1" -c "$1" -X "$2" "$API$3"; }

# login <jar> <return_to> <логин> [пароль] — весь вход; печатает, куда сервис вернул браузер (или ошибку шага)
login() {
  local jar=$1 return_to=$2 user=$3 password=${4:-$PASSWORD} h="$WORK/h" page="$WORK/page.html" url
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=$return_to"
  url=$(header "$h" location)
  [ -n "$url" ] || { echo "нет редиректа на вход"; return; }

  # Страница входа. Если в Keycloak уже есть сессия (SSO), он сразу вернёт в callback без формы.
  curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" "$url"
  url=$(header "$h" location)
  if [ -z "$url" ]; then
    local action
    action=$(python3 -c 'import re,sys,html; m=re.search(r"id=\"kc-form-login\"[^>]*action=\"([^\"]+)\"", open(sys.argv[1]).read()); print(html.unescape(m.group(1)) if m else "")' "$page")
    [ -n "$action" ] || { echo "нет формы входа"; return; }
    curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" --data-urlencode "username=$user" --data-urlencode "password=$password" "$action"
    url=$(header "$h" location)
    if [ -z "$url" ]; then
      # Текст ошибки Keycloak (тема keycloak.v2) — в элементе с классом kc-feedback-text.
      python3 -c 'import re,sys,html; m=re.search(r"kc-feedback-text[^\"]*\"[^>]*>\s*([^<]+)", open(sys.argv[1]).read()); print("keycloak: " + (html.unescape(m.group(1)).strip() if m else "осталась форма"))' "$page"
      return
    fi
  fi

  # Callback сервиса: меняет код на токены, ставит cookie и отправляет на return_to.
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$url"
  header "$h" location
}

# form_action <html> [id формы] — адрес отправки формы (html-экранирование снято)
form_action() {
  python3 -c 'import re,sys,html; m=re.search(r"<form[^>]*" + (("id=\"" + sys.argv[2] + "\"[^>]*") if len(sys.argv) > 2 else "") + r"action=\"([^\"]+)\"", open(sys.argv[1]).read()); print(html.unescape(m.group(1)) if m else "")' "$@"
}

# feedback <html> — текст сообщения Keycloak на странице (класс kc-feedback-text)
feedback() {
  python3 -c 'import re,sys,html; m=re.search(r"kc-feedback-text[^\"]*\"[^>]*>\s*([^<]+)", open(sys.argv[1]).read()); print(html.unescape(m.group(1)).strip() if m else "")' "$1"
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
