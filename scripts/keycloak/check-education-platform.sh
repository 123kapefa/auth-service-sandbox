#!/usr/bin/env bash
# Проверка этапа 6а.5 (docs/keycloak/06a-embedding.md#существующий-проект): education-platform на сервисе входа —
# realm education, ключи tag-service и content-service, структура токенов; журнал трений — 06a-friction.md.
# Запуск: стенд песочницы поднят (docker compose up -d --build --wait), затем
#   scripts/keycloak/check-education-platform.sh
# SKIP_SETUP=1 — realm уже создан (apply.sh уже запускался).
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
export API="${EDUCATION_API:-http://localhost:8083}"
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM=education
ADMIN_PHONE="${ADMIN_PHONE:-79990000001}"

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
cd "$ROOT" || exit 1

if [ -z "${SKIP_SETUP:-}" ]; then
  echo "0. Realm и ключи: samples/education/apply.sh"
  samples/education/apply.sh > "$WORK/setup.log" 2>&1
  RC=$?
  check "realm education создан, ключи выданы" "$RC $(grep -o '^Готово. Realm «education»' "$WORK/setup.log" | head -1)" "0 Готово. Realm «education»"
fi

echo "1. Realm и приложения"
MASTER=$(master_token)
check "realm education существует" \
  "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM")" 200
check "клиент education существует" \
  "$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients?clientId=education" |
     python3 -c 'import json,sys; cs=json.load(sys.stdin); print(len(cs) > 0)')" True
check "клиент education-web — публичный PKCE, redirectUri localhost:3000" \
  "$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients?clientId=education-web" |
     python3 -c 'import json,sys; c=json.load(sys.stdin)[0]; print(c["publicClient"], "localhost:3000" in " ".join(c.get("redirectUris",[])))')" "True True"
check "клиент tags существует" \
  "$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients?clientId=tags" |
     python3 -c 'import json,sys; cs=json.load(sys.stdin); print(len(cs) > 0)')" True
check "клиенты ключей зарегистрированы — service-tag и service-content" \
  "$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients" |
     python3 -c 'import json,sys; cs=[c["clientId"] for c in json.load(sys.stdin) if c["clientId"].startswith("service-")]; print(" ".join(sorted(cs)))')" "service-content service-tag"

echo "2. Токен ключа tag-service"
TAG_SECRET=$(cat "$ROOT/samples/education/.secrets/tag.key" 2>/dev/null || { echo "✗ нет .secrets/tag.key"; exit 1; })
TAG_TOKEN=$(curl -s -d grant_type=client_credentials -d client_id=service-tag -d "client_secret=$TAG_SECRET" \
  "$KC/realms/$REALM/protocol/openid-connect/token" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("access_token",""))')
check "получен access_token" "$([ -n "$TAG_TOKEN" ] && echo да)" да
[ -z "$TAG_TOKEN" ] && { echo "✗ токен ключа tag-service пуст — проверки claims пропущены"; summary; exit 1; }
check "iss — realm education" \
  "$(python3 -c 'import json,base64,sys; p=sys.argv[1].split(".")[1]; p+="="*(4-len(p)%4); print(json.loads(base64.urlsafe_b64decode(p))["iss"].split("/")[-1])' "$TAG_TOKEN")" education
check "claim client_id = service-tag (сервисный токен, не пользовательский)" \
  "$(python3 -c 'import json,base64,sys; p=sys.argv[1].split(".")[1]; p+="="*(4-len(p)%4); print(json.loads(base64.urlsafe_b64decode(p)).get("client_id",""))' "$TAG_TOKEN")" service-tag
check "claim aud содержит auth-api (доступ к сервису входа проекта)" \
  "$(python3 -c '
import json,base64,sys
p=sys.argv[1].split(".")[1]; p+="="*(4-len(p)%4)
payload=json.loads(base64.urlsafe_b64decode(p))
aud=payload.get("aud"); aud=aud if isinstance(aud,list) else [aud]
print("auth-api" in aud)' "$TAG_TOKEN")" True

check "токен ключа tag-service содержит claim role: platform-service (трение №13 — маппер)" \
  "$(python3 -c '
import json,base64,sys
p=sys.argv[1].split(".")[1]; p+="="*(4-len(p)%4)
payload=json.loads(base64.urlsafe_b64decode(p))
roles=payload.get("role",[])
roles=roles if isinstance(roles,list) else [roles]
print("platform-service" in roles)' "$TAG_TOKEN")" True

echo "3. Токен ключа content-service"
CONTENT_SECRET=$(cat "$ROOT/samples/education/.secrets/content.key" 2>/dev/null || { echo "✗ нет .secrets/content.key"; exit 1; })
CONTENT_TOKEN=$(curl -s -d grant_type=client_credentials -d client_id=service-content -d "client_secret=$CONTENT_SECRET" \
  "$KC/realms/$REALM/protocol/openid-connect/token" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("access_token",""))')
check "получен access_token content-service" "$([ -n "$CONTENT_TOKEN" ] && echo да)" да
[ -z "$CONTENT_TOKEN" ] && { echo "✗ токен ключа content-service пуст — проверки claims пропущены"; summary; exit 1; }
check "claim client_id = service-content" \
  "$(python3 -c 'import json,base64,sys; p=sys.argv[1].split(".")[1]; p+="="*(4-len(p)%4); print(json.loads(base64.urlsafe_b64decode(p)).get("client_id",""))' "$CONTENT_TOKEN")" service-content

echo "4. Сервис входа проекта принимает ключи — читает пользователей"
USER_ID=$(curl -s -H "Authorization: Bearer $TAG_TOKEN" "$API/auth/services/users?login=$ADMIN_PHONE" | python3 -c 'import json,sys; print((json.load(sys.stdin).get("result") or {}).get("id",""))' 2>/dev/null)
[ -z "$USER_ID" ] && { echo "✗ пользователь $ADMIN_PHONE не найден — запустите apply.sh без SKIP_SETUP"; summary; exit 1; }
check "service-tag читает пользователя по id — 200" \
  "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TAG_TOKEN" "$API/auth/services/users/$USER_ID")" 200
check "service-content читает пользователя по id — 200" \
  "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $CONTENT_TOKEN" "$API/auth/services/users/$USER_ID")" 200
check "без токена — 401" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$API/auth/services/users/$USER_ID")" 401

echo "5. Пользовательский токен: вход через страницы education, аудитория education"
USER_JAR="$WORK/user"; : > "$USER_JAR"
# PKCE-поток: страница входа → пароль → code → token (education-web, redirect http://localhost:3000)
VERIFIER=$(python3 -c 'import secrets; print(secrets.token_urlsafe(48))')
CHALLENGE=$(python3 -c 'import hashlib,base64,sys; v=sys.argv[1]; print(base64.urlsafe_b64encode(hashlib.sha256(v.encode()).digest()).rstrip(b"=").decode())' "$VERIFIER")
curl -s -o "$WORK/page.html" -b "$USER_JAR" -c "$USER_JAR" -G "$KC/realms/$REALM/protocol/openid-connect/auth" \
  --data-urlencode client_id=education-web --data-urlencode response_type=code --data-urlencode scope=openid \
  --data-urlencode "redirect_uri=http://localhost:3000/" --data-urlencode state=check \
  --data-urlencode "code_challenge=$CHALLENGE" --data-urlencode code_challenge_method=S256 \
  --data-urlencode platform_app=education > /dev/null
curl -s -o "$WORK/page.html" -D "$WORK/h" -b "$USER_JAR" -c "$USER_JAR" \
  --data-urlencode "username=$ADMIN_PHONE" --data-urlencode "password=$PASSWORD" \
  "$(form_action "$WORK/page.html")" > /dev/null
CODE=$(python3 -c 'import sys; from urllib.parse import urlparse, parse_qs; u=sys.argv[1]; print(parse_qs(urlparse(u).query).get("code", [""])[0])' "$(header "$WORK/h" location)" 2>/dev/null)
USER_TOKEN=$(curl -s -d grant_type=authorization_code -d client_id=education-web -d "code=$CODE" \
  --data-urlencode "redirect_uri=http://localhost:3000/" --data-urlencode "code_verifier=$VERIFIER" \
  "$KC/realms/$REALM/protocol/openid-connect/token" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("access_token",""))')
check "access_token пользователя получен (PKCE через education-web)" "$([ -n "$USER_TOKEN" ] && echo да)" да
[ -z "$USER_TOKEN" ] && { echo "✗ токен пользователя пуст — PKCE-поток не прошёл"; summary; exit 1; }
check "aудитория пользовательского токена — education" \
  "$(python3 -c '
import json,base64,sys
p=sys.argv[1].split(".")[1]; p+="="*(4-len(p)%4)
payload=json.loads(base64.urlsafe_b64decode(p))
aud=payload.get("aud"); aud=aud if isinstance(aud,list) else [aud]
print("education" in aud)' "$USER_TOKEN")" True
check "токен приложения education не годится для API сервиса входа (другой адресат) — 401" \
  "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $USER_TOKEN" "$API/me/")" 401
check "сервисный токен (client_id) — 403 на пользовательском endpoint'е" \
  "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TAG_TOKEN" "$API/me/")" 403

echo "6. Изоляция: токены другого realm не проходят"
SANDBOX_TOKEN=$(curl -s -d grant_type=password -d client_id=auth-bff -d "client_secret=${AUTH_BFF_CLIENT_SECRET:-}" \
  -d "username=71234567890" -d "password=$PASSWORD" \
  "$KC/realms/sandbox/protocol/openid-connect/token" 2>/dev/null | python3 -c 'import json,sys; print(json.load(sys.stdin).get("access_token",""))' 2>/dev/null || echo "")
if [ -n "$SANDBOX_TOKEN" ]; then
  check "токен sandbox не принимается сервисом входа education — 401" \
    "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $SANDBOX_TOKEN" "$API/me/")" 401
else
  echo "  (пропуск: пользователь sandbox не доступен — стенд не поднят или нет такого пользователя)"
fi

echo "7. Настоящие сервисы education-platform принимают наши токены (tag-service :8007, education-service :8001)"
TAG_API="${TAG_API:-http://localhost:8007}"; EDU_API="${EDU_API:-http://localhost:8001}"
if [ "$(curl -s -o /dev/null -w '%{http_code}' "$TAG_API/health/ready")" = 200 ]; then
  BODY='{"entityType":"Course","entityIds":["11111111-1111-1111-1111-111111111111"]}'
  post() { curl -s -o /dev/null -w '%{http_code}' -X POST -H 'Content-Type: application/json' -d "$2" ${3:+-H "Authorization: Bearer $3"} "$1"; }
  check "tag-service: внутренний endpoint без токена — 401" "$(post "$TAG_API/internal/search/entity-tags/batch" "$BODY")" 401
  check "tag-service: токен человека (студент) на внутреннем endpoint'е — 403" "$(post "$TAG_API/internal/search/entity-tags/batch" "$BODY" "$USER_TOKEN")" 403
  check "tag-service: токен ключа service-content на внутреннем endpoint'е — 200" "$(post "$TAG_API/internal/search/entity-tags/batch" "$BODY" "$CONTENT_TOKEN")" 200
  check "tag-service: студент без права courses.manage не создаёт тег — 403" "$(post "$TAG_API/tags" '{"title":"check","kind":"Topic"}' "$USER_TOKEN")" 403
  check "education-service: внутренний endpoint ключом service-tag — не 401 и не 403" \
    "$(post "$EDU_API/internal/issues/course-bindings" '{"issueIds":["11111111-1111-1111-1111-111111111111"]}' "$TAG_TOKEN" | grep -qE '^(401|403)$' && echo отказ || echo принят)" принят
  check "education-service: токен человека на внутреннем endpoint'е — 403" "$(post "$EDU_API/internal/issues/course-bindings" '{"issueIds":["11111111-1111-1111-1111-111111111111"]}' "$USER_TOKEN")" 403
else
  echo "  (пропуск: tag-service не запущен — docker compose -f docker-compose.yml -f docker-compose.keycloak.yml --profile infra --profile app up -d tag-service education-service)"
fi

summary
