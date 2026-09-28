#!/usr/bin/env bash
# Проверка этапа 3 (docs/keycloak/03-login.md): вход в приложение через настоящую страницу Keycloak.
# Запуск: сервис и Keycloak подняты, затем
#   scripts/keycloak/check-login.sh                            # Docker: http://localhost:8080
#   API=http://localhost:5080 scripts/keycloak/check-login.sh    # Development: dotnet run
set -uo pipefail

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

echo "Сервис: $API"
[ "$(curl -s -o /dev/null -w '%{http_code}' "$API/health/live")" = 200 ] || { echo "Сервис не отвечает на $API/health/live"; exit 1; }

echo "1. Вход (code + PKCE через страницу Keycloak)"
JAR="$WORK/jar"; : > "$JAR"
check "после входа — возврат на return_to" "$(login "$JAR" /me/ 0987654321)" /me/
check "cookie accessToken поставлена" "$([ -n "$(cookie "$JAR" accessToken)" ] && echo да || echo нет)" да
check "cookie refreshToken поставлена" "$([ -n "$(cookie "$JAR" refreshToken)" ] && echo да || echo нет)" да
check "/me/ по cookie → 200" "$(status "$JAR" GET /me/)" 200
ME=$(curl -s -b "$JAR" "$API/me/")
check "/me/: активный режим manager" "$(echo "$ME" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["activeMode"])')" manager

echo "2. Обновление токена (ротация)"
OLD_REFRESH=$(cookie "$JAR" refreshToken)
OLD_ACCESS=$(cookie "$JAR" accessToken)
check "POST /auth/refresh → 200" "$(status "$JAR" POST /auth/refresh)" 200
NEW_REFRESH=$(cookie "$JAR" refreshToken)
check "выдан новый refresh-токен" "$([ -n "$NEW_REFRESH" ] && [ "$NEW_REFRESH" != "$OLD_REFRESH" ] && echo да || echo нет)" да
check "старый refresh-токен отклонён → 401" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "Cookie: refreshToken=$OLD_REFRESH" "$API/auth/refresh")" 401

echo "3. return_to"
check "чужой return_to → возврат на /" "$(login "$JAR" https://evil.example/phish 0987654321)" /

echo "4. Выход"
LAST_REFRESH=$(cookie "$JAR" refreshToken)
check "POST /auth/logout → 200" "$(status "$JAR" POST /auth/logout)" 200
check "cookie удалены → /me/ → 401" "$(status "$JAR" GET /me/)" 401
check "refresh после выхода → 401" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "Cookie: refreshToken=$LAST_REFRESH" "$API/auth/refresh")" 401
check "сессия Keycloak закрыта — снова нужна форма" \
  "$(login "$JAR" / 0987654321 wrong-password | cut -c1-9)" keycloak:
echo "  ⓘ старый access-токен после выхода до истечения срока: $(curl -s -o /dev/null -w '%{http_code}' -H "Cookie: accessToken=$OLD_ACCESS" "$API/me/") (JWT проверяется без Keycloak — известное ограничение, этап 4)"

echo "5. Отказы"
JAR2="$WORK/jar2"; : > "$JAR2"
R=$(login "$JAR2" / 1234567890 wrong-password)
check "неверный пароль — «$R»" "$(echo "$R" | grep -q 'Неправильное имя пользователя или пароль' && echo да || echo нет)" да
JAR3="$WORK/jar3"; : > "$JAR3"
R=$(login "$JAR3" / 79990000004)
check "заблокированный пользователь — «$R»" "$(echo "$R" | grep -q 'заблокирована' && echo да || echo нет)" да

echo
echo "Итого: $PASS ✓, $FAIL ✗"
[ "$FAIL" -eq 0 ]
