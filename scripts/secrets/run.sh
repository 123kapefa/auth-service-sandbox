#!/usr/bin/env bash
# Команда с секретами из хранилища (Infisical, окружение SECRETS_ENV, по умолчанию dev) в переменных окружения:
# compose подставляет их в compose.yaml вместо значений из .env. На диск секреты не пишутся.
#   scripts/secrets/run.sh docker compose up -d --build --wait
#   scripts/secrets/run.sh scripts/keycloak/check-login.sh
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

[ $# -gt 0 ] || { echo "Использование: $0 <команда>..." >&2; exit 2; }
cd "$ROOT" || exit 1
with_secrets "$@"
