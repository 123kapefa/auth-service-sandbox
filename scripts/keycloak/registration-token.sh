#!/usr/bin/env bash
# Разрешение на регистрацию клиентов (initial access token) для сервиса входа: по нему админ проекта выдаёт ключи
# сервисам («Сервисы и ключи»). Выдаёт техник мастер-админкой Keycloak; живёт в Keycloak — после пересоздания стенда
# выдать заново. Запуск:
#   scripts/keycloak/registration-token.sh                 — напечатать токен
#   scripts/keycloak/registration-token.sh --write-env     — записать в .env (KEYCLOAK_REGISTRATION_TOKEN) и перезапустить сервис
#   scripts/keycloak/registration-token.sh --store         — записать в хранилище секретов (docs/secrets.md), перезапустить
#                                                            сервис и отозвать прежние разрешения — это и есть смена
# COUNT — сколько клиентов можно зарегистрировать (по умолчанию 100), EXPIRATION — срок в секундах (0 — бессрочно).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM="${REALM:-sandbox}"
# shellcheck source=master-token.sh
source "$(dirname "${BASH_SOURCE[0]}")/master-token.sh"
if [ "${1:-}" = "--store" ]; then
  # shellcheck source=../secrets/lib.sh
  source "$ROOT/scripts/secrets/lib.sh"
  KEYCLOAK_ADMIN_PASSWORD="${KEYCLOAK_ADMIN_PASSWORD:-$(secret_get KEYCLOAK_ADMIN_PASSWORD)}"
fi

MASTER=$(master_token)
[ -n "$MASTER" ] || { echo "Нет токена мастер-админки (KEYCLOAK_ADMIN_PASSWORD в .env)" >&2; exit 1; }
ISSUED=$(curl -sf -X POST -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' \
  -d "{\"count\":${COUNT:-100},\"expiration\":${EXPIRATION:-0}}" "$KC/admin/realms/$REALM/clients-initial-access")
TOKEN=$(echo "$ISSUED" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')

if [ "${1:-}" = "--write-env" ]; then
  if grep -q '^KEYCLOAK_REGISTRATION_TOKEN=' "$ROOT/.env"; then
    python3 - "$ROOT/.env" "$TOKEN" <<'PY'
import re, sys
path, token = sys.argv[1], sys.argv[2]
text = open(path).read()
open(path, "w").write(re.sub(r"(?m)^KEYCLOAK_REGISTRATION_TOKEN=.*$", "KEYCLOAK_REGISTRATION_TOKEN=" + token, text))
PY
  else
    printf '\nKEYCLOAK_REGISTRATION_TOKEN=%s\n' "$TOKEN" >> "$ROOT/.env"
  fi
  docker compose --project-directory "$ROOT" up -d --force-recreate --wait auth-service > /dev/null 2>&1
  echo "Разрешение записано в .env, сервис перезапущен (клиентов: ${COUNT:-100})"
elif [ "${1:-}" = "--store" ]; then
  secret_set "KEYCLOAK_REGISTRATION_TOKEN=$TOKEN"
  restart auth-service
  # Прежние разрешения отзываются только после перезапуска: до него сервис выдаёт ключи по старому.
  KEEP=$(echo "$ISSUED" | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')
  for id in $(curl -sf -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients-initial-access" |
      python3 -c 'import json,sys; print("\n".join(t["id"] for t in json.load(sys.stdin)))'); do
    [ "$id" = "$KEEP" ] || curl -sf -o /dev/null -X DELETE -H "Authorization: Bearer $MASTER" \
      "$KC/admin/realms/$REALM/clients-initial-access/$id"
  done
  echo "Разрешение записано в хранилище ($SECRETS_ENV), сервис перезапущен, прежние разрешения отозваны"
else
  echo "$TOKEN"
fi
