#!/usr/bin/env bash
# Магазин на сервисе входа (этап 6а.4, docs/keycloak/06a-embedding.md#магазин-заказы-и-уведомления) — всё, что сделал
# бы новый проект, по шагам и теми же средствами: проект и его сервис входа, приложение «Заказы» с правами и ролями,
# сотрудник и покупатель, ключи «Заказов» и «Уведомлений», запуск обоих сервисов.
# Запуск: стенд песочницы поднят (docker compose up -d --build --wait), затем
#   samples/shop/setup.sh
# Повторный запуск — без ошибок: сделанное пропускается, ключи выдаются заново (секреты — в .env).
# SKIP_BUILD=1 — образы магазина уже собраны (не собирать пакеты и образы).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
export API="${DEMO_SHOP_API:-http://localhost:8082}"
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM=demo-shop
ADMIN_PHONE=79995550001
MANAGER_PHONE=79995550002
BUYER_PHONE=79995550003
PROJECT=(docker compose --project-directory "$ROOT" -f "$ROOT/compose.yaml" -f "$ROOT/infra/projects/demo-shop/compose.yaml")
SHOP=("${PROJECT[@]}" -f "$ROOT/samples/shop/compose.yaml")

# shellcheck source=../../scripts/keycloak/lib.sh
source "$ROOT/scripts/keycloak/lib.sh"
cd "$ROOT"

# admin_api <метод> <путь> [тело] — API админки сервиса входа проекта от имени админа; печатает «<код> <JSON>»
admin_api() {
  local args=(-s -o "$WORK/r.json" -w '%{http_code}' -b "$JAR" -c "$JAR" -X "$1")
  [ -n "${3:-}" ] && args+=(-H 'Content-Type: application/json' -d "$3")
  printf '%s ' "$(curl "${args[@]}" "$API$2")"
  cat "$WORK/r.json"; echo
}
expect() { case "$1" in "$2"*) ;; *) echo "✗ $3: $1" >&2; exit 1 ;; esac; }

echo "1. Проект «Демо-магазин» и его сервис входа"
MASTER=$(master_token)
if [ "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM")" != 200 ]; then
  DEV=1 AUTH_BFF_CLIENT_SECRET=demo-shop-bff-secret scripts/project/create-project.sh "$REALM" "Демо-магазин" "$API" \
    http://auth-service-demo-shop:8080 | head -1
fi
scripts/project/add-admin.py "$REALM" "$ADMIN_PHONE" --email admin@demo-shop.test --password "$PASSWORD" | cut -d' ' -f1-4
scripts/project/add-app.py "$REALM" orders "Заказы" --default-role customer --web http://localhost:5300 | cut -d: -f1
# Ключи сервисам выдаёт сервис входа проекта — ему нужно разрешение на регистрацию клиентов.
DEMO_SHOP_REGISTRATION_TOKEN=$(REALM=$REALM scripts/keycloak/registration-token.sh) \
  "${PROJECT[@]}" up -d --force-recreate --wait auth-service-demo-shop > /dev/null 2>&1
for _ in $(seq 30); do [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")" = 200 ] && break; sleep 2; done
echo "Сервис входа проекта: $API"

echo "2. Права и роли «Заказов» — в админке проекта"
JAR="$WORK/admin"; : > "$JAR"
expect "$(login "$JAR" /admin/ "$ADMIN_PHONE")" /admin/ "вход админа проекта"
for permission in "orders.view:Свои заказы" "orders.create:Оформить заказ" "orders.manage:Все заказы и статусы"; do
  R=$(admin_api POST /auth/admin/apps/orders/permissions "{\"name\":\"${permission%%:*}\",\"description\":\"${permission#*:}\"}")
  case "$R" in 201*|200*|409*) ;; *) expect "$R" 201 "право ${permission%%:*}" ;; esac
done
expect "$(admin_api PUT /auth/admin/apps/orders/roles/customer '{"description":"Покупатель","permissions":["orders.view","orders.create"]}')" 200 "роль customer"
MANAGER_ROLE='{"description":"Сотрудник магазина","permissions":["orders.view","orders.manage"]}'
R=$(admin_api POST /auth/admin/apps/orders/roles "{\"name\":\"manager\",${MANAGER_ROLE:1}")
case "$R" in 409*) R=$(admin_api PUT /auth/admin/apps/orders/roles/manager "$MANAGER_ROLE") ;; esac
expect "$R" 20 "роль manager"
echo "customer — orders.view, orders.create; manager — orders.view, orders.manage"

echo "3. Сотрудник и покупатель (пароль $PASSWORD)"
# Учётки — мастер-админкой (в бою люди регистрируются сами: вход по коду). Роль сотрудника — в админке проекта.
user_id() {
  python3 - "$REALM" "$1" "$2" "$PASSWORD" <<'PY'
import sys
sys.path.insert(0, "scripts/project")
from keycloak_admin import Admin
realm, phone, email, password = sys.argv[1:]
admin = Admin(realm)
admin.call("POST", "/users", {"username": phone, "email": email, "emailVerified": True, "enabled": True,
                              "attributes": {"phone": [f"+{phone}"], "active_mode": ["customer"]},
                              "credentials": [{"type": "password", "value": password, "temporary": False}]}, ok=(409,))
print(admin.call("GET", f"/users?username={phone}&exact=true")[0]["id"])
PY
}
MANAGER_ID=$(user_id "$MANAGER_PHONE" manager@demo-shop.test)
user_id "$BUYER_PHONE" buyer@demo-shop.test > /dev/null
expect "$(admin_api PUT "/auth/admin/apps/orders/users/$MANAGER_ID/roles" '{"roles":["manager"]}')" 200 "роль manager сотруднику"
# Режим в «Заказах» человеку самому не переключить (журнал трений) — ставим сразу: сотрудник работает как manager.
curl -s -o /dev/null -X PUT -H "Authorization: Bearer $(master_token)" -H 'Content-Type: application/json' \
  -d "$(curl -s -H "Authorization: Bearer $(master_token)" "$KC/admin/realms/$REALM/users/$MANAGER_ID" |
    python3 -c 'import json,sys; u=json.load(sys.stdin); a=u.get("attributes") or {}; a["active_mode.orders"]=["manager"]; print(json.dumps({**u,"attributes":a}))')" \
  "$KC/admin/realms/$REALM/users/$MANAGER_ID"
echo "сотрудник +$MANAGER_PHONE (manager@demo-shop.test) — режим manager; покупатель +$BUYER_PHONE (buyer@demo-shop.test)"

echo "4. Ключи сервисов — «Сервисы и ключи»"
# issue_key <код> <название> <права JSON> — отозвать прежний и выдать заново; печатает секрет
issue_key() {
  admin_api DELETE "/auth/admin/services/$1" > /dev/null
  local r
  r=$(admin_api POST /auth/admin/services "{\"code\":\"$1\",\"name\":\"$2\",\"permissions\":$3}")
  expect "$r" 200 "ключ $1"
  python3 -c 'import json,sys; print(json.loads(sys.argv[1].split(" ",1)[1])["result"]["clientSecret"])' "$r"
}
# set_env <имя> <значение> — в .env стенда (секрет ключа — только в окружении сервиса)
set_env() {
  python3 - "$ROOT/.env" "$1" "$2" <<'PY'
import re, sys
path, name, value = sys.argv[1:]
text = open(path).read()
line = f"{name}={value}"
text = re.sub(rf"(?m)^{name}=.*$", line, text) if re.search(rf"(?m)^{name}=", text) else text.rstrip("\n") + "\n" + line + "\n"
open(path, "w").write(text)
PY
}
ORDERS_SECRET=$(issue_key orders "Заказы" '[]')
NOTIFICATIONS_SECRET=$(issue_key notifications "Уведомления" '["service.users.read"]')
set_env SHOP_ORDERS_KEY_SECRET "$ORDERS_SECRET"
set_env SHOP_NOTIFICATIONS_KEY_SECRET "$NOTIFICATIONS_SECRET"
echo "service-orders — без прав в сервисе входа; service-notifications — service.users.read; секреты — в .env"

echo "5. «Заказы» и «Уведомления»"
if [ -z "${SKIP_BUILD:-}" ]; then
  scripts/package/pack.sh > /dev/null
  "${SHOP[@]}" build shop-orders shop-notifications > /dev/null
fi
"${SHOP[@]}" up -d --no-build --force-recreate shop-orders shop-notifications 2>&1 | grep -iv "^ *\(container\|network\)" || true
for port in 5300 5310; do
  for _ in $(seq 30); do [ "$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:$port/health/live")" = 200 ] && break; sleep 1; done
done
cat <<INFO
Готово: магазин — http://localhost:5300 (вход: +$BUYER_PHONE покупатель, +$MANAGER_PHONE сотрудник, пароль $PASSWORD;
новый покупатель — «Войти по коду», код в логе Keycloak). Письма — http://localhost:8025.
INFO
