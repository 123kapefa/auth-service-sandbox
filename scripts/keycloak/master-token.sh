# shellcheck shell=bash
# master_token — токен мастер-админки Keycloak (логин и пароль — из окружения или .env). Берётся изнутри контейнера
# Keycloak (kcadm.sh, запрос с localhost): master-realm требует HTTPS для «внешних» адресов, а запрос с хоста может
# прийти не с частного адреса (VPN, прокси). Пользоваться токеном можно и с хоста.
# Другой compose (deploy/compose.yaml): COMPOSE_FILE, COMPOSE_PROJECT_NAME и KEYCLOAK_SERVICE=auth-keycloak;
# пароль — KEYCLOAK_ADMIN_PASSWORD (или AUTH_KEYCLOAK_ADMIN_PASSWORD). Не вышло — печатает пустую строку (код 0).
master_token() {
  local root
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
  # Пароль — переменной окружения KC_CLI_PASSWORD (kcadm берёт его оттуда), не аргументом: аргументы видны в ps.
  # Файл с токенами — удаляется.
  KC_CLI_PASSWORD="${KEYCLOAK_ADMIN_PASSWORD:-${AUTH_KEYCLOAK_ADMIN_PASSWORD:-$(grep -s '^KEYCLOAK_ADMIN_PASSWORD=' "$root/.env" | cut -d= -f2-)}}" \
  KC_ADMIN_USER="${KEYCLOAK_ADMIN:-${AUTH_KEYCLOAK_ADMIN:-admin}}" \
    docker compose --project-directory "$root" exec -T -e KC_CLI_PASSWORD -e KC_ADMIN_USER "${KEYCLOAK_SERVICE:-keycloak}" sh -c '
      config=$(mktemp)
      /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8180 --realm master \
        --user "$KC_ADMIN_USER" --config "$config" > /dev/null 2>&1 && cat "$config"
      rm -f "$config"' 2>/dev/null |
    python3 -c '
import json,sys
try: print(next(iter(json.load(sys.stdin)["endpoints"].values()))["master"]["token"])
except Exception: print("")'
}
