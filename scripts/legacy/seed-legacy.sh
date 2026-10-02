#!/usr/bin/env bash
# «Боевая» база для спайка этапа 6 (docs/keycloak/06-bcrypt-migration.md): отдельная база legacy_auth в Postgres
# песочницы, таблица users со схемой боевого auth-service и выдуманные пользователи. Пересоздаёт таблицу каждый раз.
#   scripts/legacy/seed-legacy.sh            — 9 пользователей на все случаи переноса (infra/legacy/seed.sql)
#   scripts/legacy/seed-legacy.sh --bulk N   — плюс N покупателей +7997XXXXXXX (пароль Legacy-1!) — замерить время переноса
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BULK=0
[ "${1:-}" = "--bulk" ] && BULK="${2:?нужно число: --bulk 1000}"

# psql_in <база> [ключи psql…] — SQL со стандартного ввода, внутри контейнера postgres
psql_in() {
  local db="$1"; shift
  docker compose --project-directory "$ROOT" exec -T postgres sh -c 'psql -v ON_ERROR_STOP=1 -q -U "$POSTGRES_USER" -d "$0" "$@"' "$db" "$@"
}

# База — отдельная, как у боевого сервиса: инструмент переноса читает её по своей строке подключения.
if [ -z "$(echo "SELECT 1 FROM pg_database WHERE datname = 'legacy_auth'" | psql_in postgres -tA)" ]; then
  echo "CREATE DATABASE legacy_auth" | psql_in postgres
fi

cat "$ROOT/infra/legacy/schema.sql" "$ROOT/infra/legacy/seed.sql" | psql_in legacy_auth

if [ "$BULK" -gt 0 ]; then
  psql_in legacy_auth <<SQL
INSERT INTO users ("Id", "Phone", "Role", "LoginCount", "PasswordHash", "PasswordSetAtUtc", "CreatedAt", "UpdatedAt")
SELECT gen_random_uuid(), '+7997' || lpad(i::text, 7, '0'), 'customer', 1,
       '\$2a\$12\$oTSY.ajW6NkI0GFoqNLp9.dcm/Fk.icpb.TFe1fQY20qAwdP72awq', now(), now(), now()
FROM generate_series(1, $BULK) AS i;
SQL
fi

echo "legacy_auth.users: $(echo 'SELECT count(*) FROM users' | psql_in legacy_auth -tA) пользователей"
