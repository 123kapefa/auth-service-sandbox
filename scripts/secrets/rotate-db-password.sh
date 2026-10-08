#!/usr/bin/env bash
# Смена пароля базы (docs/secrets.md#пароли-баз): пароль меняется в Postgres (ALTER ROLE), затем в хранилище, и
# сервисы перезапускаются с новым. Открытые соединения пула продолжают работать, новые до перезапуска не откроются —
# перезапуск сразу следом, отказов почти нет.
#   scripts/secrets/rotate-db-password.sh auth        — база сервиса входа (POSTGRES_PASSWORD; мигратор и сервис, мигратор — как зависимость сервиса)
#   scripts/secrets/rotate-db-password.sh keycloak    — база Keycloak (KEYCLOAK_DB_PASSWORD; Keycloak)
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

case "${1:-}" in
  auth) NAME=POSTGRES_PASSWORD DB_SERVICE=postgres ROLE=$({ grep -s '^POSTGRES_USER=' "$ROOT/.env" || true; } | cut -d= -f2-); ROLE=${ROLE:-auth_service}
        SERVICES=(auth-service) ;;
  keycloak) NAME=KEYCLOAK_DB_PASSWORD DB_SERVICE=keycloak-postgres ROLE=keycloak SERVICES=(keycloak) ;;
  *) echo "Использование: $0 auth|keycloak" >&2; exit 2 ;;
esac

NEW=$(random_secret)
# Пароль — переменной окружения контейнера (\getenv psql), не аргументом и не в тексте команды: аргументы видны
# в списке процессов.
printf '\\getenv password PGPASSWORD_NEW\nalter role "%s" password :'"'"'password'"'"';\n' "$ROLE" |
  PGPASSWORD_NEW="$NEW" compose_exec -e PGPASSWORD_NEW "$DB_SERVICE" psql -U "$ROLE" -d postgres -v ON_ERROR_STOP=1 -q
secret_set "$NAME=$NEW"
restart "${SERVICES[@]}"
echo "Готово: пароль роли $ROLE сменён в базе и в хранилище ($NAME)"
