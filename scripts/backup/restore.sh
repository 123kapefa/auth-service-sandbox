#!/usr/bin/env bash
# Восстановление обеих баз сервиса входа из копии (deploy/backup/backup.sh) в Docker Compose: песочница (compose.yaml в корне) или
# поставка (deploy/compose.yaml). Порядок и что делать в Kubernetes — docs/migrations.md, «Резервные копии».
#
#   scripts/backup/restore.sh --dir <каталог с копиями> [--stamp <метка>] [--stand | --deploy] --yes
#
#   --dir     каталог с парами <auth-service|keycloak>-<метка>.dump (+ .sha256)
#   --stamp   метка пары (20261123T030000Z); без неё — самая свежая пара, у которой есть обе копии и совпадают суммы
#   --stand   песочница: compose.yaml в корне, базы postgres и keycloak-postgres (по умолчанию)
#   --deploy  поставка: COMPOSE_FILE или deploy/compose.yaml (каталог проекта — AUTH_PROJECT_DIR, по умолчанию deploy/),
#             базы auth-db и auth-keycloak-db
#   --yes     подтверждение: текущее содержимое обеих баз будет СТЁРТО и заменено копией
#
# Что делает: проверяет суммы; останавливает Keycloak и сервис; стирает схемы обеих баз и разворачивает копию (pg_restore; с чистой
# пустой базы — тот же путь); запускает Keycloak и сервис и ждёт готовности. Копия Keycloak берётся только вместе с копией сервиса
# той же метки: люди и сессии в Keycloak, журнал и ключи сервисов — в базе сервиса. Redis (отметки отзыва токенов) не копируется:
# отзыв, сделанный между копией и сбоем, пропадает — токены живут до 15 минут.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
MODE=stand; DIR=""; STAMP=""; YES=""
while [ $# -gt 0 ]; do
  case "$1" in
    --dir) DIR=${2:?нужен каталог}; shift 2 ;;
    --stamp) STAMP=${2:?нужна метка}; shift 2 ;;
    --stand) MODE=stand; shift ;;
    --deploy) MODE=deploy; shift ;;
    --yes) YES=1; shift ;;
    *) echo "Неизвестный аргумент: $1 (см. заголовок скрипта)" >&2; exit 2 ;;
  esac
done
[ -d "$DIR" ] || { echo "Нужен --dir: каталог с копиями" >&2; exit 2; }

if [ "$MODE" = deploy ]; then
  DC=(docker compose --project-directory "${AUTH_PROJECT_DIR:-$ROOT/deploy}")
  [ -n "${COMPOSE_FILE:-}" ] || DC+=(-f "$ROOT/deploy/compose.yaml")
  AUTH_DB_SVC=auth-db; AUTH_DB_USER=auth_service; AUTH_DB_NAME=auth_service
  KC_DB_SVC=auth-keycloak-db; KC_DB_USER=keycloak; KC_DB_NAME=keycloak
  APPS=(auth-service auth-keycloak)
else
  DC=(docker compose --project-directory "$ROOT")
  # Пользователь и база сервиса — из .env песочницы (как в compose.yaml).
  env_value() { { grep -E "^$1=" "$ROOT/.env" 2> /dev/null || true; } | tail -1 | cut -d= -f2-; }
  AUTH_DB_SVC=postgres; AUTH_DB_USER=${POSTGRES_USER:-$(env_value POSTGRES_USER)}; AUTH_DB_NAME=${POSTGRES_DB:-$(env_value POSTGRES_DB)}
  KC_DB_SVC=keycloak-postgres; KC_DB_USER=keycloak; KC_DB_NAME=keycloak
  APPS=(auth-service keycloak)
fi

# Пустые имена (нет .env) обнаруживаем до остановки сервисов, а не после.
for v in AUTH_DB_USER AUTH_DB_NAME KC_DB_USER KC_DB_NAME; do
  [ -n "${!v}" ] || { echo "Не определено $v (для --stand нужен .env с POSTGRES_USER и POSTGRES_DB)" >&2; exit 2; }
done

sha_check() { # sha_check <файл .sha256> — в каталоге файла
  if command -v sha256sum > /dev/null; then (cd "$(dirname "$1")" && sha256sum -c "$(basename "$1")" > /dev/null 2>&1)
  else (cd "$(dirname "$1")" && shasum -a 256 -c "$(basename "$1")" > /dev/null 2>&1); fi
}
pair_ok() { # pair_ok <метка>
  local f
  for f in "$DIR/auth-service-$1.dump" "$DIR/keycloak-$1.dump"; do
    [ -s "$f" ] && [ -f "$f.sha256" ] && sha_check "$f.sha256" || return 1
  done
}

if [ -z "$STAMP" ]; then
  for f in $(ls -1t "$DIR"/auth-service-*.dump 2> /dev/null); do
    candidate=$(basename "$f" .dump); candidate=${candidate#auth-service-}
    if pair_ok "$candidate"; then STAMP=$candidate; break; fi
  done
  [ -n "$STAMP" ] || { echo "В $DIR нет целой пары копий (обе базы, суммы совпадают)" >&2; exit 1; }
fi
pair_ok "$STAMP" || { echo "Пара $STAMP неполная или повреждена (нет файла или не совпала сумма)" >&2; exit 1; }
echo "Копия: $STAMP (суммы совпали)"
[ -n "$YES" ] || { echo "Это сотрёт текущее содержимое обеих баз. Добавьте --yes, если так и нужно." >&2; exit 2; }

# Сбой посреди восстановления: сервисы остановлены, а базы могут быть стёрты или заменены наполовину. Повторный запуск с той же копией безопасен.
trap 'rc=$?; [ "$rc" -eq 0 ] || echo "ОШИБКА (код $rc): восстановление не завершено. Keycloak и сервис остановлены, базы могут быть пустыми или заменены частично. Повторите: scripts/backup/restore.sh --dir \"$DIR\" --stamp $STAMP ${MODE:+--$MODE} --yes" >&2' EXIT

echo "1. Остановка Keycloak и сервиса"
"${DC[@]}" stop "${APPS[@]}" > /dev/null
"${DC[@]}" up -d --wait "$AUTH_DB_SVC" "$KC_DB_SVC" > /dev/null

restore_db() { # restore_db <сервис> <пользователь> <база> <файл>
  echo "2. База $3 из $(basename "$4")"
  # Стираем все схемы: копия должна заменить базу целиком, а не лечь поверх (таблицы новее копии остались бы лишними).
  "${DC[@]}" exec -T "$1" psql -q -v ON_ERROR_STOP=1 -U "$2" -d "$3" -c "
    DO \$\$ DECLARE s text; BEGIN
      FOR s IN SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg\\_%' AND nspname <> 'information_schema' LOOP
        EXECUTE format('DROP SCHEMA %I CASCADE', s);
      END LOOP;
      CREATE SCHEMA public;
    END \$\$;" > /dev/null
  "${DC[@]}" exec -T "$1" pg_restore --single-transaction --exit-on-error --no-owner --no-privileges -U "$2" -d "$3" < "$4"
}
restore_db "$AUTH_DB_SVC" "$AUTH_DB_USER" "$AUTH_DB_NAME" "$DIR/auth-service-$STAMP.dump"
restore_db "$KC_DB_SVC" "$KC_DB_USER" "$KC_DB_NAME" "$DIR/keycloak-$STAMP.dump"

echo "3. Запуск Keycloak и сервиса"
"${DC[@]}" up -d --wait "${APPS[@]}" > /dev/null
echo "Готово: базы восстановлены из копии $STAMP."
