#!/usr/bin/env bash
# Смена секрета клиента BFF (sandbox-bff; в проекте — auth-bff) без потери сессий и без отказов во входе
# (docs/secrets.md#секрет-bff). Сессии от секрета не зависят (refresh-токены привязаны к клиенту, не к секрету),
# отказать может только обмен кода и refresh в промежутке, когда сервис и Keycloak знают разные секреты, — поэтому:
#   1. в хранилище: секрет — новый, прежний — в <имя>_PREVIOUS; сервис перезапускается и знает оба
#      (Keycloak отверг новый — повторяет со старым);
#   2. в Keycloak секрет клиента меняется на новый — сервис сразу проходит с ним;
#   3. прежний удаляется из хранилища, сервис перезапускается только с новым.
# Запуск (стенд): scripts/secrets/rotate-bff-secret.sh
# Проект на deploy/compose.yaml: SECRET_NAME=AUTH_BFF_CLIENT_SECRET CLIENT_ID=auth-bff REALM=<код проекта>
#   ADMIN_SECRET_NAME=AUTH_KEYCLOAK_ADMIN_PASSWORD KEYCLOAK_SERVICE=auth-keycloak KC=<адрес Keycloak> COMPOSE_FILE=...
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
# shellcheck source=../keycloak/master-token.sh
source "$ROOT/scripts/keycloak/master-token.sh"

NAME="${SECRET_NAME:-SANDBOX_BFF_CLIENT_SECRET}"
CLIENT="${CLIENT_ID:-sandbox-bff}"
REALM="${REALM:-sandbox}"
SERVICE="${SERVICE:-auth-service}"
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"

OLD=$(secret_get "$NAME")
[ -n "$OLD" ] || { echo "В хранилище ($SECRETS_ENV) нет $NAME" >&2; exit 1; }
NEW=$(random_secret)

echo "1. Сервис знает новый и прежний секрет"
secret_set "$NAME=$NEW" "${NAME}_PREVIOUS=$OLD"
restart "$SERVICE"

echo "2. Новый секрет — в Keycloak (клиент $CLIENT, проект $REALM)"
# Пароль мастер-админки — из хранилища; master_token берёт его из KEYCLOAK_ADMIN_PASSWORD.
KEYCLOAK_ADMIN_PASSWORD=$(secret_get "${ADMIN_SECRET_NAME:-KEYCLOAK_ADMIN_PASSWORD}")
export KEYCLOAK_ADMIN_PASSWORD
MASTER=$(master_token)
[ -n "$MASTER" ] || { echo "Нет токена мастер-админки Keycloak; сервис работает с обоими секретами — повторите шаг 2" >&2; exit 1; }
ID=$(curl -sf -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients?clientId=$CLIENT" |
  python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])')
curl -sf -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients/$ID" |
  NEW_SECRET="$NEW" python3 -c 'import json,os,sys; c=json.load(sys.stdin); c["secret"]=os.environ["NEW_SECRET"]; print(json.dumps(c))' |
  curl -sf -o /dev/null -X PUT -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' --data-binary @- \
    "$KC/admin/realms/$REALM/clients/$ID"

echo "3. Прежний секрет убран"
secret_delete "${NAME}_PREVIOUS"
restart "$SERVICE"
echo "Готово: $NAME сменён в хранилище и в Keycloak"
