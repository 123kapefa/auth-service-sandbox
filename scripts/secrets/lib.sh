# shellcheck shell=bash
# Общее для scripts/secrets/*: доступ к хранилищу секретов Infisical (docs/secrets.md) через его CLI (infisical).
# Откуда брать секреты:
#   INFISICAL_API_URL      — сервер (стенд: http://localhost:8280/api; облако: https://app.infisical.com/api;
#                            ru-облако или свой сервер компании — его адрес);
#   INFISICAL_PROJECT_ID   — проект auth-service в Infisical;
#   SECRETS_ENV            — окружение: dev (стенд), staging, prod (по умолчанию dev);
#   вход — INFISICAL_TOKEN (готовый токен) или учётка машины INFISICAL_CLIENT_ID + INFISICAL_CLIENT_SECRET
#   (Universal Auth: обменивается на токен на час). На стенде всё это пишет infisical-local.sh в .secrets/infisical.env
#   (не в git), в бою — задаёт платформа (Kubernetes — оператор Infisical, без этого файла).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SECRETS_FILE="$ROOT/.secrets/infisical.env"
# Значения из файла — только если не заданы в окружении.
if [ -f "$SECRETS_FILE" ]; then
  while IFS='=' read -r name value; do
    [[ "$name" =~ ^[A-Z_][A-Z0-9_]*$ ]] || continue
    [ -n "${!name:-}" ] || export "$name=$value"
  done < "$SECRETS_FILE"
fi
export INFISICAL_API_URL="${INFISICAL_API_URL:-http://localhost:${INFISICAL_PORT:-8280}/api}"
SECRETS_ENV="${SECRETS_ENV:-dev}"
# Подсказки CLI и его телеметрия — не нужны в скриптах.
export INFISICAL_DISABLE_UPDATE_CHECK=true

command -v infisical > /dev/null || {
  echo "Нужен Infisical CLI: https://infisical.com/docs/cli/overview (brew install infisical/get-cli/infisical)" >&2
  exit 1
}

# ensure_token — получить токен доступа к хранилищу в INFISICAL_TOKEN (один раз на скрипт). CLI берёт его из
# переменной сам; аргументом не передаётся: аргументы видны в списке процессов.
ensure_token() {
  [ -z "${INFISICAL_TOKEN:-}" ] || return 0
  [ -n "${INFISICAL_CLIENT_ID:-}" ] && [ -n "${INFISICAL_CLIENT_SECRET:-}" ] || {
    echo "Нет входа в Infisical: INFISICAL_TOKEN или INFISICAL_CLIENT_ID + INFISICAL_CLIENT_SECRET" \
      "(стенд — scripts/secrets/infisical-local.sh up)" >&2
    exit 1
  }
  [ -n "${INFISICAL_PROJECT_ID:-}" ] || { echo "Нет INFISICAL_PROJECT_ID" >&2; exit 1; }
  INFISICAL_TOKEN=$(infisical login --method=universal-auth --client-id="$INFISICAL_CLIENT_ID" \
    --client-secret="$INFISICAL_CLIENT_SECRET" --silent --plain --telemetry=false)
  export INFISICAL_TOKEN
}

# Общие аргументы CLI: проект, окружение.
_args() { ARGS=(--projectId="$INFISICAL_PROJECT_ID" --env="$SECRETS_ENV" --silent --telemetry=false); }

# secret_get <имя> — значение секрета (нет — пустая строка)
secret_get() {
  ensure_token; _args
  infisical secrets get "$1" "${ARGS[@]}" --plain 2> /dev/null || true
}

# secret_set <имя>=<значение>... — записать секреты. Значения не печатаются (CLI выводит их таблицей).
secret_set() {
  ensure_token; _args
  infisical secrets set "${ARGS[@]}" "$@" > /dev/null
}

# secret_delete <имя>... — удалить секреты (нет — не ошибка)
secret_delete() {
  ensure_token; _args
  local name
  for name in "$@"; do
    infisical secrets delete "$name" "${ARGS[@]}" --type shared > /dev/null 2>&1 || true
  done
}

# with_secrets <команда>... — выполнить команду с секретами окружения в переменных (только у неё, не в файлах)
with_secrets() {
  ensure_token; _args
  infisical run "${ARGS[@]}" --expand=false -- "$@"
}

# random_secret — секрет из [A-Za-z0-9_-] (env-файлы и строки подключения не ломаются на «$» и «;»)
random_secret() { python3 -c 'import secrets; print(secrets.token_urlsafe(32))'; }

# new_pfx <пароль> — новый сертификат DataProtection: PFX в base64 (new-pfx.sh)
new_pfx() { PFX_PASSWORD="$1" "$ROOT/scripts/secrets/new-pfx.sh"; }

# restart <сервис>... — пересоздать сервисы compose с секретами из хранилища (compose пересоздаёт контейнер, если
# изменились его переменные) и дождаться готовности. Другой compose — COMPOSE_FILE, COMPOSE_PROJECT_NAME.
# У сервиса входа в compose нет healthcheck — его готовность ждём по /health/ready (HEALTH_URL).
restart() {
  (cd "$ROOT" && with_secrets docker compose up -d --wait "$@" > /dev/null 2>&1)
  [[ " $* " == *" auth-service "* ]] || return 0
  local _
  for _ in $(seq 1 60); do
    [ "$(curl -s -o /dev/null -w '%{http_code}' "${HEALTH_URL:-http://localhost:8080/health/ready}")" = 200 ] && return 0
    sleep 1
  done
  echo "Сервис входа не готов после перезапуска" >&2
  return 1
}

# compose_exec <сервис> <команда>... — команда в контейнере сервиса
compose_exec() { (cd "$ROOT" && with_secrets docker compose exec -T "$@"); }
