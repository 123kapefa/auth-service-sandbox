#!/usr/bin/env bash
# Новый проект для сервиса входа: realm из шаблона (scripts/project/new-project.py) создаётся в Keycloak мастер-админкой.
# Дальше — экземпляр сервиса входа проекта (те же образ и настройки, другой realm) и приложения проекта
# (scripts/project/add-app.sh). Запуск:
#   scripts/project/create-project.sh <realm> "<Название>" <внешний адрес сервиса> [адрес сервиса изнутри сети Keycloak]
# Секрет клиента auth-bff — из AUTH_BFF_CLIENT_SECRET или случайный (печатается).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM="${1:?realm: код проекта}"; NAME="${2:?название проекта}"; SERVICE_URL="${3:?внешний адрес сервиса входа проекта}"
BACKCHANNEL_URL="${4:-$SERVICE_URL}"
SECRET="${AUTH_BFF_CLIENT_SECRET:-$(python3 -c 'import secrets; print(secrets.token_urlsafe(32))')}"
# shellcheck source=../keycloak/master-token.sh
source "$ROOT/scripts/keycloak/master-token.sh"

MASTER=$(master_token)
[ -n "$MASTER" ] || { echo "Нет токена мастер-админки (KEYCLOAK_ADMIN_PASSWORD в .env)" >&2; exit 1; }
if [ "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM")" = 200 ]; then
  echo "Проект (realm) $REALM уже есть" >&2; exit 1
fi

JSON=$(mktemp); trap 'rm -f "$JSON"' EXIT
"$ROOT/scripts/project/new-project.py" --realm "$REALM" --name "$NAME" --service-url "$SERVICE_URL" \
  --backchannel-url "$BACKCHANNEL_URL" --secret "$SECRET" ${DEV:+--dev} > "$JSON"
CODE=$(curl -s -o /dev/stderr -w '%{http_code}' -X POST -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' \
  --data-binary @"$JSON" "$KC/admin/realms")
[ "$CODE" = 201 ] || { echo "Keycloak не создал realm: $CODE" >&2; exit 1; }

cat <<INFO
Проект «$NAME» создан: realm $REALM, клиенты auth-api и auth-bff.
Сервис входа проекта — тот же образ, настройки:
  Authentication__Authority=$KC/realms/$REALM
  Authentication__Audience=auth-api
  Keycloak__ClientId=auth-bff
  Keycloak__ClientSecret=$SECRET
  Keycloak__RegistrationToken=<scripts/keycloak/registration-token.sh, REALM=$REALM>
Приложения проекта — scripts/project/add-app.sh $REALM <client_id> "<Название>"
INFO
