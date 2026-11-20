#!/usr/bin/env bash
# Существующий проект: education-platform (docs/embedding-guide.md, раздел «Пример: education-platform») —
# подключить их сервисы к нашему сервису входа. Создаёт realm «education», добавляет приложение «Education» и
# «Теги» (только права ключей), выдаёт ключи tag-service и content-service (education-service), выводит env-переменные
# для docker-compose.keycloak.yml в archforge-testbed/education-platform.
# Запуск: стенд песочницы поднят (docker compose up -d --build --wait), затем
#   samples/education/apply.sh
# Повторный запуск — без ошибок: сделанное пропускается, ключи выдаются заново.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
export API="${EDUCATION_API:-http://localhost:8083}"
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM=education
ADMIN_PHONE="${ADMIN_PHONE:-79990000001}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@education.test}"
PROJECT=(docker compose --project-directory "$ROOT" -f "$ROOT/compose.yaml" -f "$ROOT/infra/projects/education/compose.yaml")

# shellcheck source=../../scripts/keycloak/lib.sh
source "$ROOT/scripts/keycloak/lib.sh"
cd "$ROOT"

admin_api() {
  local args=(-s -o "$WORK/r.json" -w '%{http_code}' -b "$JAR" -c "$JAR" -X "$1")
  [ -n "${3:-}" ] && args+=(-H 'Content-Type: application/json' -d "$3")
  printf '%s ' "$(curl "${args[@]}" "$API$2")"
  cat "$WORK/r.json"; echo
}
expect() { case "$1" in "$2"*) ;; *) echo "✗ $3: $1" >&2; exit 1 ;; esac; }

echo "1. Проект «Education Platform» и его сервис входа"
MASTER=$(master_token)
if [ "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM")" != 200 ]; then
  DEV=1 AUTH_BFF_CLIENT_SECRET=education-bff-secret scripts/project/create-project.sh "$REALM" "Education Platform" "$API" \
    http://auth-service-education:8080 | head -1
fi
scripts/project/add-admin.py "$REALM" "$ADMIN_PHONE" --email "$ADMIN_EMAIL" --password "$PASSWORD" | cut -d' ' -f1-4
# Приложение «Education» — основной клиент; education-web — PKCE для Next.js фронтенда.
scripts/project/add-app.py "$REALM" education "Education" --default-role student --web http://localhost:3000 | cut -d: -f1
# «Теги» — приложение без входа людей: только права для ключа tag-service.
scripts/project/add-app.py "$REALM" tags "Теги" | cut -d: -f1
# Ключи выдаёт сервис входа проекта — ему нужен регистрационный токен.
EDUCATION_REGISTRATION_TOKEN=$(REALM=$REALM scripts/keycloak/registration-token.sh) \
  "${PROJECT[@]}" up -d --force-recreate --wait auth-service-education > /dev/null 2>&1
for _ in $(seq 30); do [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")" = 200 ] && break; sleep 2; done
echo "Сервис входа проекта: $API"

echo "2. Ключи сервисов"
JAR="$WORK/admin"; : > "$JAR"
expect "$(login "$JAR" /admin/ "$ADMIN_PHONE")" /admin/ "вход админа проекта"

# Сервисы education-platform проверяют aud=education. Адресат приложения попадает в токен ключа, только если у ключа есть
# право в этом приложении (трение №2/№17) — заводим служебное право и выдаём его ключам.
expect "$(admin_api POST /auth/admin/apps/education/permissions '{"name":"internal.access","description":"Вызовы между сервисами"}' | sed 's/^409.*/200 уже есть/')" 200 "право education:internal.access"

issue_key() {
  admin_api DELETE "/auth/admin/services/$1" > /dev/null
  local r
  r=$(admin_api POST /auth/admin/services "{\"code\":\"$1\",\"name\":\"$2\",\"permissions\":$3}")
  expect "$r" 200 "ключ $1"
  python3 -c 'import json,sys; print(json.loads(sys.argv[1].split(" ",1)[1])["result"]["clientSecret"])' "$r"
}
# Секреты — файлами (для check-education-platform.sh и для docker-compose.keycloak.yml через .env).
SECRETS="$ROOT/samples/education/.secrets"
mkdir -p "$SECRETS"
save_secret() { printf '%s' "$2" > "$SECRETS/$1.key"; chmod 644 "$SECRETS/$1.key"; }
# tag-service: читает пользователей и объявляет права/роли приложения «Теги».
TAG_SECRET=$(issue_key tag "TagService" '["service.users.read","service.catalog.declare","education:internal.access"]')
save_secret tag "$TAG_SECRET"
# content-service (EducationContentService): читает пользователей и объявляет права/роли «Education».
CONTENT_SECRET=$(issue_key content "EducationContentService" '["service.users.read","service.catalog.declare","education:internal.access"]')
save_secret content "$CONTENT_SECRET"
echo "service-tag — service.users.read, service.catalog.declare, education:internal.access"
echo "service-content — service.users.read, service.catalog.declare, education:internal.access"
echo "секреты — samples/education/.secrets"

echo "3. Маппер platform-service: образование-platform ищет роль в токене ключа (трение №13)"
# education-platform проверяет RequireAnyRole("platform-service") во всех internal/-endpoint'ах.
# Наш токен ключа роли не содержит — добавляем hardcoded mapper к каждому ключу напрямую в Keycloak.
# Маппер привязан к клиенту Keycloak: ключ, выданный заново из админки, придёт без него (внутренние endpoint'ы — 403).
# Лечение — повторный запуск apply.sh; check-education-platform.sh ловит это проверкой role в токенах обоих ключей.
add_service_role_mapper() {
  local client_id="service-$1"
  local kc_id
  kc_id=$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients?clientId=$client_id" |
    python3 -c 'import json,sys; cs=json.load(sys.stdin); print(cs[0]["id"] if cs else "")' 2>/dev/null)
  [ -z "$kc_id" ] && { echo "  ключ $client_id не найден в Keycloak — пропускаем"; return; }
  # Идемпотентно: проверяем, не добавлен ли уже маппер
  local existing
  existing=$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients/$kc_id/protocol-mappers/models" |
    python3 -c 'import json,sys; print(any(m["name"]=="platform-service-role" for m in json.load(sys.stdin)))' 2>/dev/null)
  if [ "$existing" = "True" ]; then
    echo "  $client_id — маппер уже есть"
    return
  fi
  # oidc-hardcoded-claim-mapper — пишет произвольный claim прямо в токен (в отличие от oidc-hardcoded-role-mapper,
  # который пишет в realm_access.roles, а не в flat claim «role»). Без multivalued — одна строка.
  curl -s -o /dev/null -w '%{http_code}' -X POST \
    -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' \
    "$KC/admin/realms/$REALM/clients/$kc_id/protocol-mappers/models" \
    -d '{
      "name":"platform-service-role",
      "protocol":"openid-connect",
      "protocolMapper":"oidc-hardcoded-claim-mapper",
      "consentRequired":false,
      "config":{
        "claim.name":"role",
        "claim.value":"platform-service",
        "jsonType.label":"String",
        "access.token.claim":"true",
        "id.token.claim":"false",
        "userinfo.token.claim":"false",
        "introspection.token.claim":"true"
      }
    }' | python3 -c 'import sys; code=sys.stdin.read().strip(); print(f"  {sys.argv[1]} — маппер добавлен ({code})")' - "$client_id"
}
MASTER=$(master_token)
add_service_role_mapper tag
add_service_role_mapper content

cat <<ENV

# --- Добавьте в archforge-testbed/education-platform/.env ---
KEYCLOAK_PUBLIC=http://localhost:8180
KEYCLOAK_INTERNAL=http://host.docker.internal:8180
SERVICE_KEY_TAG=$TAG_SECRET
SERVICE_KEY_CONTENT=$CONTENT_SECRET
# ------------------------------------------------------------
ENV

cat <<INFO
Готово. Realm «education» на http://localhost:8180, сервис входа проекта — http://localhost:8083.
Дальше в education-platform:
  docker compose -f docker-compose.yml -f docker-compose.keycloak.yml --profile infra --profile app up -d tag-service education-service
  docker compose -f docker-compose.yml -f docker-compose.keycloak.yml --profile infra --profile frontend up -d frontend
INFO
