#!/usr/bin/env bash
# Секреты стенда песочницы — в хранилище (окружение dev проекта auth-service в Infisical, docs/secrets.md).
#   scripts/secrets/seed.sh               — значения из .env (стенд уже поднят с ними: пароли баз и секрет BFF записаны
#                                           в томах при первом запуске), недостающие — новые случайные
#   scripts/secrets/seed.sh --new         — все значения новые (только для чистого стенда: docker compose down -v)
#   scripts/secrets/seed.sh --strip-env   — после записи убрать эти секреты из .env: на машине их больше нет
# Дальше стенд поднимается с секретами из хранилища: scripts/secrets/run.sh docker compose up -d --wait.
# SECRETS_ENV=staging|prod — другое окружение (для них seed не нужен: секреты заводит человек, docs/secrets.md).
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

NEW=false; STRIP=false
for arg in "$@"; do
  case "$arg" in
    --new) NEW=true ;;
    --strip-env) STRIP=true ;;
    *) echo "Неизвестный параметр: $arg" >&2; exit 2 ;;
  esac
done

# Обязательные: нет в .env — новые случайные. Необязательные: только если заданы в .env (ключи внешних систем).
REQUIRED=(POSTGRES_PASSWORD KEYCLOAK_ADMIN_PASSWORD KEYCLOAK_DB_PASSWORD SANDBOX_BFF_CLIENT_SECRET LEGACY_IMPORT_CLIENT_SECRET)
OPTIONAL=(KEYCLOAK_REGISTRATION_TOKEN YANDEX_CLIENT_SECRET SMARTCAPTCHA_SERVER_KEY PHONE_PROVIDER_URL SMTP_PASSWORD)

from_env() { $NEW && return 0; { grep -s "^$1=" "$ROOT/.env" || true; } | tail -1 | cut -d= -f2-; }

PAIRS=()
for name in "${REQUIRED[@]}"; do
  value=$(from_env "$name")
  PAIRS+=("$name=${value:-$(random_secret)}")
done
for name in "${OPTIONAL[@]}"; do
  value=$(from_env "$name")
  [ -z "$value" ] || PAIRS+=("$name=$value")
done
# Сертификат DataProtection: на стенде раньше не было — ключи в базе перешифруются им при старте сервиса.
if $NEW || [ -z "$(secret_get AUTH_DATA_PROTECTION_CERT)" ]; then
  password=$(random_secret)
  PAIRS+=("AUTH_DATA_PROTECTION_CERT_PASSWORD=$password" "AUTH_DATA_PROTECTION_CERT=$(new_pfx "$password")")
fi

secret_set "${PAIRS[@]}"
echo "В хранилище ($SECRETS_ENV): ${PAIRS[*]%%=*}"

if $STRIP && [ -f "$ROOT/.env" ]; then
  python3 - "$ROOT/.env" "${REQUIRED[@]}" "${OPTIONAL[@]}" <<'PY'
import re, sys
path, names = sys.argv[1], set(sys.argv[2:])
lines = open(path).read().splitlines(keepends=True)
kept = [l for l in lines if not (m := re.match(r"([A-Z_][A-Z0-9_]*)=", l)) or m.group(1) not in names]
open(path, "w").write("".join(kept))
print(f"Из .env убрано строк: {len(lines) - len(kept)}")
PY
fi
