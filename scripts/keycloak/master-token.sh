# shellcheck shell=bash
# master_token — токен мастер-админки Keycloak (логин и пароль — из окружения или .env). Берётся изнутри контейнера
# Keycloak (kcadm.sh, запрос с localhost): master-realm требует HTTPS для «внешних» адресов, а запрос с хоста может
# прийти не с частного адреса (VPN, прокси). Пользоваться токеном можно и с хоста.
master_token() {
  local root password
  root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
  password="${KEYCLOAK_ADMIN_PASSWORD:-$(grep -s '^KEYCLOAK_ADMIN_PASSWORD=' "$root/.env" | cut -d= -f2)}"
  docker compose --project-directory "$root" exec -T keycloak /opt/keycloak/bin/kcadm.sh config credentials \
    --server http://localhost:8180 --realm master --user "${KEYCLOAK_ADMIN:-admin}" --password "$password" \
    --config /tmp/kcadm-check.config > /dev/null 2>&1 || return
  docker compose --project-directory "$root" exec -T keycloak cat /tmp/kcadm-check.config |
    python3 -c 'import json,sys; print(next(iter(json.load(sys.stdin)["endpoints"].values()))["master"]["token"])'
}
