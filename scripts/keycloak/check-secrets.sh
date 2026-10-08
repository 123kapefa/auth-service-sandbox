#!/usr/bin/env bash
# Проверка 11.5 (docs/secrets.md): стенд — с секретами из хранилища Infisical, смена секрета BFF и сертификата
# DataProtection — без потери сессий: вход, сделанный до смены, обновляется после неё, а вход, начатый до смены
# сертификата (state в cookie зашифрован DataProtection), завершается после неё. Секреты меняются по-настоящему.
# Запуск: scripts/secrets/infisical-local.sh up, scripts/secrets/seed.sh --strip-env,
#   scripts/secrets/run.sh docker compose up -d --build --wait, затем
#   scripts/keycloak/check-secrets.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
# shellcheck source=../secrets/lib.sh
source "$(dirname "$0")/../secrets/lib.sh"
set +e
KC="http://localhost:${KEYCLOAK_PORT:-8180}"
SECRETS="$ROOT/scripts/secrets"

require_service

# same <a> <b> — совпадают ли значения (сами секреты не печатаются)
same() { [ -n "$1" ] && [ "$1" = "$2" ] && echo да || echo нет; }
container_env() { compose_exec auth-service printenv "$1" 2> /dev/null; }
sql() { compose_exec postgres psql -U "${POSTGRES_USER:-auth_service}" -d auth_service -tAc "$1"; }
# client_token <секрет> — HTTP-код client_credentials клиента sandbox-bff с этим секретом
client_token() {
  curl -s -o /dev/null -w '%{http_code}' -d grant_type=client_credentials -d client_id=sandbox-bff \
    --data-urlencode "client_secret=$1" "$KC/realms/sandbox/protocol/openid-connect/token"
}

echo "1. Стенд — с секретами из хранилища ($SECRETS_ENV)"
check "секрет BFF в сервисе — из хранилища" \
  "$(same "$(container_env Keycloak__ClientSecret)" "$(secret_get SANDBOX_BFF_CLIENT_SECRET)")" да
check "сертификат DataProtection в сервисе — из хранилища" \
  "$(same "$(container_env DataProtection__Certificate)" "$(secret_get AUTH_DATA_PROTECTION_CERT)")" да
check "в .env нет секретов" \
  "$(grep -cE '^(POSTGRES_PASSWORD|KEYCLOAK_ADMIN_PASSWORD|KEYCLOAK_DB_PASSWORD|SANDBOX_BFF_CLIENT_SECRET)=' "$ROOT/.env")" 0
JAR="$WORK/jar"; : > "$JAR"
check "вход" "$(login "$JAR" /me/ 0987654321)" /me/
check "ключи DataProtection в базе — под сертификатом" \
  "$(sql "select count(*) from auth.data_protection_keys where xml not like '%EncryptedData%'")" 0

echo "2. Смена секрета BFF"
OLD_SECRET=$(secret_get SANDBOX_BFF_CLIENT_SECRET)
"$SECRETS/rotate-bff-secret.sh" | sed 's/^/     /'
NEW_SECRET=$(secret_get SANDBOX_BFF_CLIENT_SECRET)
check "секрет в хранилище новый" "$([ -n "$NEW_SECRET" ] && [ "$NEW_SECRET" != "$OLD_SECRET" ] && echo да || echo нет)" да
check "прежнего в хранилище нет" "$([ -z "$(secret_get SANDBOX_BFF_CLIENT_SECRET_PREVIOUS)" ] && echo да || echo нет)" да
check "Keycloak принимает новый секрет" "$(client_token "$NEW_SECRET")" 200
check "Keycloak не принимает прежний" "$(client_token "$OLD_SECRET")" 401
check "сессия до смены: refresh → 200" "$(status "$JAR" POST /auth/refresh)" 200
check "сессия до смены: /me/ → 200" "$(status "$JAR" GET /me/)" 200
JAR2="$WORK/jar2"; : > "$JAR2"
check "новый вход" "$(login "$JAR2" /me/ 0987654321)" /me/

echo "3. Смена сертификата DataProtection"
# Вход начат до смены: state (PKCE и return_to) — в cookie, зашифрованной ключом DataProtection.
JAR3="$WORK/jar3"; : > "$JAR3"
CALLBACK=$(login_until_done "$JAR3" /me/ 0987654321 "$PASSWORD")
OLD_CERT=$(secret_get AUTH_DATA_PROTECTION_CERT)
"$SECRETS/rotate-data-protection-cert.sh" | sed 's/^/     /'
check "сертификат в хранилище новый" \
  "$([ "$(secret_get AUTH_DATA_PROTECTION_CERT)" != "$OLD_CERT" ] && echo да || echo нет)" да
check "прежнего в хранилище нет" "$([ -z "$(secret_get AUTH_DATA_PROTECTION_PREVIOUS_CERT)" ] && echo да || echo нет)" да
check "сервис — только с новым сертификатом" "$([ -z "$(container_env DataProtection__PreviousCertificate)" ] && echo да || echo нет)" да
check "вход, начатый до смены, завершается" \
  "$(curl -s -o /dev/null -D "$WORK/h" -b "$JAR3" -c "$JAR3" "$CALLBACK"; header "$WORK/h" location)" /me/
check "сессия до смены: refresh → 200" "$(status "$JAR" POST /auth/refresh)" 200
check "сессия до смены: /me/ → 200" "$(status "$JAR" GET /me/)" 200
check "все ключи в базе — под сертификатом" \
  "$(sql "select count(*) from auth.data_protection_keys where xml not like '%EncryptedData%'")" 0

summary
