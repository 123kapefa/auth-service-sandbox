#!/usr/bin/env bash
# Хранилище секретов стенда: свой сервер Infisical в Docker (infra/infisical/compose.yaml, http://localhost:8280).
#   scripts/secrets/infisical-local.sh up     — поднять; в первый раз — подготовить: админ стенда, проект auth-service
#                                               (окружения dev, staging, prod), учётка машины auth-service-stand
#   scripts/secrets/infisical-local.sh down   — остановить (данные — в томе; down -v удалит и их)
# Всё, что нужно для входа, — в .secrets/infisical.env (не в git, права 600): ключи самого Infisical, пароль админа
# (вход в веб-интерфейс), INFISICAL_PROJECT_ID и учётка машины (INFISICAL_CLIENT_ID / INFISICAL_CLIENT_SECRET).
# Это «нулевой» секрет стенда: им открывается хранилище, поэтому в нём он лежать не может. Дальше — scripts/secrets/seed.sh.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
FILE="$ROOT/.secrets/infisical.env"
COMPOSE=(docker compose -f "$ROOT/infra/infisical/compose.yaml" --env-file "$FILE")
PORT="${INFISICAL_PORT:-8280}"
API="http://localhost:$PORT/api"

put() { printf '%s=%s\n' "$1" "$2" >> "$FILE"; }
get() { grep -s "^$1=" "$FILE" | tail -1 | cut -d= -f2-; }

case "${1:-up}" in
  down) "${COMPOSE[@]}" down; exit 0 ;;
  up) ;;
  *) echo "Использование: $0 up|down" >&2; exit 2 ;;
esac

mkdir -p "$ROOT/.secrets"; chmod 700 "$ROOT/.secrets"
touch "$FILE"; chmod 600 "$FILE"
[ -n "$(get INFISICAL_ENCRYPTION_KEY)" ] || {
  put INFISICAL_DB_PASSWORD "$(python3 -c 'import secrets; print(secrets.token_urlsafe(24))')"
  put INFISICAL_ENCRYPTION_KEY "$(openssl rand -hex 16)"
  put INFISICAL_AUTH_SECRET "$(openssl rand -base64 32)"
}

"${COMPOSE[@]}" --progress quiet up -d --wait
echo "Infisical: http://localhost:$PORT"
[ -z "$(get INFISICAL_CLIENT_SECRET)" ] || { echo "Уже подготовлен (.secrets/infisical.env)"; exit 0; }

command -v infisical > /dev/null || { echo "Нужен Infisical CLI: https://infisical.com/docs/cli/overview" >&2; exit 1; }
EMAIL=admin@sandbox.local
PASSWORD=$(python3 -c 'import secrets; print(secrets.token_urlsafe(18))')
BOOT=$(infisical bootstrap --domain "http://localhost:$PORT" --email "$EMAIL" --password "$PASSWORD" \
  --organization sandbox --output json --silent --telemetry=false) || {
  echo "Сервер уже подготовлен, а учётки стенда в .secrets/infisical.env нет: $0 down, docker volume rm" \
    "sandbox-infisical_infisical_db и заново" >&2
  exit 1
}
put INFISICAL_ADMIN_EMAIL "$EMAIL"
put INFISICAL_ADMIN_PASSWORD "$PASSWORD"
ADMIN=$(echo "$BOOT" | python3 -c 'import json,sys; print(json.load(sys.stdin)["identity"]["credentials"]["token"])')
ORG=$(echo "$BOOT" | python3 -c 'import json,sys; print(json.load(sys.stdin)["organization"]["id"])')

# api <метод> <путь> <json> — запрос от админа стенда; печатает ответ, ошибка — выход
api() {
  curl -sf -X "$1" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d "$3" "$API$2" ||
    { echo "Infisical: $1 $2 не прошёл" >&2; exit 1; }
}
field() { python3 -c "import json,sys; print(json.load(sys.stdin)$1)"; }

PROJECT=$(api POST /v1/projects '{"projectName":"auth-service","slug":"auth-service","type":"secret-manager"}' | field '["project"]["id"]')
put INFISICAL_PROJECT_ID "$PROJECT"

# Учётка машины стенда: роль в проекте member — читает и пишет секреты (скрипты смены секретов пишут в хранилище).
# В бою учётка сервиса только читает своё окружение, а пишет человек под своим входом (docs/secrets.md).
IDENTITY=$(api POST /v1/identities "{\"name\":\"auth-service-stand\",\"organizationId\":\"$ORG\",\"role\":\"no-access\"}" | field '["identity"]["id"]')
CLIENT_ID=$(api POST "/v1/auth/universal-auth/identities/$IDENTITY" '{"accessTokenTTL":3600,"accessTokenMaxTTL":86400}' |
  field '["identityUniversalAuth"]["clientId"]')
CLIENT_SECRET=$(api POST "/v1/auth/universal-auth/identities/$IDENTITY/client-secrets" '{"description":"stand"}' | field '["clientSecret"]')
api POST "/v1/projects/$PROJECT/memberships/identities/$IDENTITY" '{"role":"member"}' > /dev/null
put INFISICAL_CLIENT_ID "$CLIENT_ID"
put INFISICAL_CLIENT_SECRET "$CLIENT_SECRET"

echo "Подготовлен: проект auth-service (dev, staging, prod), вход в веб-интерфейс — $EMAIL, пароль в .secrets/infisical.env."
echo "Дальше: scripts/secrets/seed.sh — положить секреты стенда в хранилище."
