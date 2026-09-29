#!/usr/bin/env bash
# Проверка этапа 3 (docs/keycloak/03-login.md): вход в приложение через настоящую страницу Keycloak.
# Запуск: сервис и Keycloak подняты, затем
#   scripts/keycloak/check-login.sh                            # Docker: http://localhost:8080
#   API=http://localhost:5080 scripts/keycloak/check-login.sh    # Development: dotnet run
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

require_service

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

summary
