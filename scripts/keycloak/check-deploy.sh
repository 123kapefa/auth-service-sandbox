#!/usr/bin/env bash
# Проверка этапа 6а (docs/keycloak/06a-embedding.md): поставка как компонент — deploy/compose.yaml только из образов,
# в боевом режиме. Как у чужого проекта: свой compose-файл подключает deploy/compose.yaml через include: со своим
# env-файлом, перед сервисами — прокси с https (Caddy со своим локальным сертификатом). Keycloak — start за прокси,
# проект — без DEV (sslRequired external, https), сервис — Production с сертификатом DataProtection, не root.
# Создаёт проект теми же скриптами, входит админом, выдаёт ключ сервиса, перезапускает сервис и перевыпускает ключ —
# и всё удаляет (свои тома). Стенд песочницы не трогает. Нужны образы: scripts/package/build-images.sh. Запуск:
#   scripts/keycloak/check-deploy.sh            (KEEP=1 — не удалять установку после проверки)
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
REALM=deploy-shop
ADMIN_PHONE=79995550101
# Снаружи — только через прокси (https); напрямую, на 127.0.0.1, — Keycloak для скриптов техника (мастер-админка).
API="https://localhost:18444"
KC_PUBLIC="https://localhost:18443"
KC="http://localhost:18180"
VERSION=$(sed -n 's#.*<VersionPrefix>\(.*\)</VersionPrefix>.*#\1#p' "$ROOT/Directory.Build.props" | head -1)

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

# Файлы установки — рядом с репозиторием (.gitignore): при KEEP=1 по ним установку можно остановить.
DIR="$ROOT/.check-deploy"
rm -rf "$DIR"; mkdir -p "$DIR"
export COMPOSE_PROJECT_NAME=platform-auth-check
export COMPOSE_FILE="$DIR/compose.yaml"
export KEYCLOAK_SERVICE=auth-keycloak KEYCLOAK_ADMIN=admin KEYCLOAK_ADMIN_PASSWORD=check-admin-password KC
SECRET=deploy-shop-bff-secret

# Сертификат DataProtection (PFX в base64) — как в .env.example; в бою — из хранилища секретов.
DP_CERT=$(PFX_PASSWORD=check-pfx "$ROOT/scripts/secrets/new-pfx.sh")

cat > "$DIR/auth.env" <<ENV
AUTH_VERSION=$VERSION
AUTH_KEYCLOAK_URL=$KC_PUBLIC
AUTH_KEYCLOAK_PORT=18180
AUTH_SERVICE_PORT=18080
AUTH_KEYCLOAK_ADMIN=admin
AUTH_KEYCLOAK_ADMIN_PASSWORD=check-admin-password
AUTH_KEYCLOAK_DB_PASSWORD=check-keycloak-db
AUTH_DB_PASSWORD=check-auth-db
AUTH_REALM=$REALM
AUTH_BFF_CLIENT_SECRET=$SECRET
AUTH_KEYCLOAK_METADATA_URL=http://auth-keycloak:8180/realms/$REALM/.well-known/openid-configuration
AUTH_REQUIRE_HTTPS=false
AUTH_ALLOWED_HOSTS=localhost
AUTH_RETURN_ORIGIN=$API
AUTH_DATA_PROTECTION_CERT=$DP_CERT
AUTH_DATA_PROTECTION_CERT_PASSWORD=check-pfx
ENV

cat > "$DIR/Caddyfile" <<CADDY
{
	local_certs
	skip_install_trust
}
$KC_PUBLIC {
	reverse_proxy auth-keycloak:8180
}
$API {
	reverse_proxy auth-service:8080
}
CADDY

# Пути абсолютные: скрипты зовут compose с --project-directory корня репозитория.
cat > "$DIR/compose.yaml" <<COMPOSE
include:
  - path: $ROOT/deploy/compose.yaml
    env_file: $DIR/auth.env
services:
  auth-proxy:
    image: caddy:2-alpine
    ports:
      - "127.0.0.1:18443:18443"
      - "127.0.0.1:18444:18444"
    volumes:
      - $DIR/Caddyfile:/etc/caddy/Caddyfile:ro
      - auth_proxy_data:/data
volumes:
  auth_proxy_data:
COMPOSE

dc() { docker compose --project-directory "$ROOT" "$@"; }
cleanup() {
  if [ -n "${KEEP:-}" ]; then
    echo "Установка оставлена: COMPOSE_PROJECT_NAME=$COMPOSE_PROJECT_NAME docker compose -f .check-deploy/compose.yaml down -v"
  else
    dc down -v > /dev/null 2>&1
    rm -rf "$DIR"
  fi
  rm -rf "$WORK"
}
trap cleanup EXIT

# У сервиса нет healthcheck в образе: --wait ждёт только запуска контейнера — готовность ждём сами.
ready() { for _ in $(seq 1 60); do [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")" = 200 ] && { echo 200; return; }; sleep 1; done; echo нет; }

echo "0. Установка из образов: прокси с https, Keycloak, проект, сервис"
dc down -v > /dev/null 2>&1
check "Keycloak из образа поднялся в боевом режиме (start)" \
  "$(dc up -d --wait auth-keycloak auth-proxy > /dev/null 2>&1 && docker inspect --format '{{join .Config.Cmd " "}}' "$(dc ps -q auth-keycloak)")" start
# Корневой сертификат прокси — curl доверяет ему, как браузер — сертификату компании.
for _ in $(seq 1 30); do dc cp auth-proxy:/data/caddy/pki/authorities/local/root.crt "$DIR/proxy-ca.crt" > /dev/null 2>&1 && break; sleep 1; done
export CURL_CA_BUNDLE="$DIR/proxy-ca.crt"
check "Keycloak за прокси по https: issuer — внешний адрес" \
  "$(curl -s "$KC_PUBLIC/realms/master/.well-known/openid-configuration" | python3 -c 'import json,sys; print(json.load(sys.stdin)["issuer"])' 2>&1 | tail -1)" "$KC_PUBLIC/realms/master"
check "проект создан скриптом (без DEV)" \
  "$(AUTH_KEYCLOAK_URL=$KC_PUBLIC AUTH_BFF_CLIENT_SECRET=$SECRET "$ROOT/scripts/project/create-project.sh" "$REALM" "Проверка поставки" "$API" http://auth-service:8080 2>/dev/null | head -1)" \
  "Проект «Проверка поставки» создан: realm $REALM, клиенты auth-api и auth-bff."
check "проект: https обязателен для внешних адресов, почты нет" \
  "$(curl -s -H "Authorization: Bearer $(master_token)" "$KC/admin/realms/$REALM" | python3 -c 'import json,sys; r=json.load(sys.stdin); print(r["sslRequired"], bool(r.get("smtpServer")))')" "external False"
check "первый админ" "$("$ROOT/scripts/project/add-admin.py" "$REALM" "$ADMIN_PHONE" --password "$PASSWORD" | cut -d' ' -f1-4)" "Админ проекта $REALM: +$ADMIN_PHONE"
echo "AUTH_REGISTRATION_TOKEN=$(REALM=$REALM "$ROOT/scripts/keycloak/registration-token.sh")" >> "$DIR/auth.env"
check "сервис входа из образа поднялся (мигратор отработал)" "$(dc up -d --wait auth-service > /dev/null 2>&1 && ready)" 200
check "образ сервиса — версия из Directory.Build.props" \
  "$(docker inspect --format '{{.Config.Image}}' "$(dc ps -q auth-service)")" "platform-auth-service:$VERSION"
check "сервис — не root" "$(dc exec -T auth-service id -u 2>/dev/null)" 1654

echo "1. Вход и админка (через прокси, https)"
JAR="$WORK/page-jar"; : > "$JAR"
curl -s -o /dev/null -D "$WORK/h" -b "$JAR" -c "$JAR" "$API/auth/login?return_to=/admin/"
curl -s -o "$WORK/page.html" -b "$JAR" -c "$JAR" "$(header "$WORK/h" location)"
check "страница входа — realm проекта, тема platform, по https" \
  "$(kc "$WORK/page.html" 'c["realm"]["name"] + " " + c["url"]["resourcesUrl"].split("/")[-1] + " " + c["realm"]["displayName"] + " " + c["url"]["loginAction"].split(":")[0]')" \
  "$REALM platform Проверка поставки https"
ADMIN="$WORK/admin"; : > "$ADMIN"
check "админ проекта вошёл через сервис из образа" "$(login "$ADMIN" /admin/ "$ADMIN_PHONE")" /admin/
# Файл cookie curl: 4-я колонка — Secure (строки HttpOnly начинаются с «#HttpOnly_», комментарии — с «# »).
check "все cookie (сервиса и Keycloak) — только по https" "$(awk 'NF >= 7 && $1 != "#" { print $4 }' "$ADMIN" | sort -u | tr '\n' ' ' | sed 's/ $//')" TRUE
check "админка отдаётся" "$(curl -s -o /dev/null -w '%{http_code}' -b "$ADMIN" "$API/admin/services")" 200
check "«кто я»: админ проекта" \
  "$(curl -s -b "$ADMIN" "$API/auth/admin/me" | python3 -c 'import json,sys; print("services.manage" in json.load(sys.stdin)["result"]["permissions"])')" True

echo "2. Ключ сервиса"
service_token() {
  curl -s -d grant_type=client_credentials -d client_id=service-orders --data-urlencode "client_secret=$1" "$KC_PUBLIC/realms/$REALM/protocol/openid-connect/token" |
    python3 -c 'import json,sys; print(json.load(sys.stdin).get("access_token",""))'
}
R=$(curl -s -b "$ADMIN" -H 'Content-Type: application/json' -d '{"code":"orders","name":"Заказы","permissions":["service.users.read"]}' "$API/auth/admin/services")
ORDERS=$(echo "$R" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["clientSecret"])' 2>/dev/null)
check "ключ выдан" "$([ -n "$ORDERS" ] && echo да || echo "нет: $R")" да
check "сервис по ключу читает админа проекта" \
  "$(curl -s -H "Authorization: Bearer $(service_token "$ORDERS")" "$API/auth/services/users?login=$ADMIN_PHONE" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["phone"])')" "+$ADMIN_PHONE"
check "ключи DataProtection в базе зашифрованы сертификатом" \
  "$(dc exec -T auth-db psql -U auth_service -d auth_service -tAc "SELECT count(*) FILTER (WHERE xml LIKE '%EncryptedData%') || '/' || count(*) FROM auth.data_protection_keys" 2>&1)" "1/1"

echo "3. Перезапуск сервиса: ключи DataProtection — из базы, расшифровка сертификатом"
dc restart auth-service > /dev/null 2>&1
check "сервис снова готов" "$(ready)" 200
R=$(curl -s -b "$ADMIN" -X POST "$API/auth/admin/services/orders/rotate")
ROTATED=$(echo "$R" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["clientSecret"])' 2>/dev/null)
check "ключ перевыпущен (токен управления расшифрован после перезапуска)" "$([ -n "$ROTATED" ] && [ "$ROTATED" != "$ORDERS" ] && echo да || echo "нет: $R")" да
check "старый ключ не принимается, новый — да" \
  "$([ -z "$(service_token "$ORDERS")" ] && echo нет || echo да) $([ -n "$(service_token "$ROTATED")" ] && echo да || echo нет)" "нет да"

summary
