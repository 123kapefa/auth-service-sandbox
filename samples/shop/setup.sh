#!/usr/bin/env bash
# Магазин на сервисе входа (этап 6а.4, docs/keycloak/06a-embedding.md#магазин-заказы-и-уведомления) — всё, что сделал
# бы новый проект, по шагам и теми же средствами (руководство — docs/embedding-guide.md): проект и его сервис входа,
# приложения «Заказы» и «Уведомления», ключи обоих, запуск сервисов (права и роли каждый объявляет сам, из кода), право
# «Уведомлений» ключу «Заказов», сотрудник и покупатель.
# Запуск: стенд песочницы поднят (docker compose up -d --build --wait), затем
#   samples/shop/setup.sh
# Повторный запуск — без ошибок: сделанное пропускается, ключи выдаются заново (секреты — samples/shop/.secrets).
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
# «Уведомления» — приложение без людей: только права, которые выдают ключам других сервисов.
scripts/project/add-app.py "$REALM" notifications "Уведомления" | cut -d: -f1
# Ключи сервисам выдаёт сервис входа проекта — ему нужно разрешение на регистрацию клиентов.
DEMO_SHOP_REGISTRATION_TOKEN=$(REALM=$REALM scripts/keycloak/registration-token.sh) \
  "${PROJECT[@]}" up -d --force-recreate --wait auth-service-demo-shop > /dev/null 2>&1
for _ in $(seq 30); do [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")" = 200 ] && break; sleep 2; done
echo "Сервис входа проекта: $API"

echo "2. Ключи сервисов — «Сервисы и ключи»"
JAR="$WORK/admin"; : > "$JAR"
expect "$(login "$JAR" /admin/ "$ADMIN_PHONE")" /admin/ "вход админа проекта"

# issue_key <код> <название> <права JSON> — отозвать прежний и выдать заново; печатает секрет
issue_key() {
  admin_api DELETE "/auth/admin/services/$1" > /dev/null
  local r
  r=$(admin_api POST /auth/admin/services "{\"code\":\"$1\",\"name\":\"$2\",\"permissions\":$3}")
  expect "$r" 200 "ключ $1"
  python3 -c 'import json,sys; print(json.loads(sys.argv[1].split(" ",1)[1])["result"]["clientSecret"])' "$r"
}
# Секрет ключа — файлом (секрет compose): сервис перечитывает его, когда берёт новый токен. Файл пишется на месте (тот
# же inode) — контейнер видит новый секрет без перезапуска.
SECRETS="$ROOT/samples/shop/.secrets"
mkdir -p "$SECRETS"
save_secret() { printf '%s' "$2" > "$SECRETS/$1.key"; chmod 644 "$SECRETS/$1.key"; }
# Обоим — право объявлять права и роли своего приложения: их каталог — в коде (AddAppCatalog в Program.cs).
save_secret orders "$(issue_key orders "Заказы" '["service.catalog.declare"]')"
save_secret notifications "$(issue_key notifications "Уведомления" '["service.users.read","service.catalog.declare"]')"
echo "service-orders — service.catalog.declare; service-notifications — service.users.read, service.catalog.declare;"
echo "секреты — samples/shop/.secrets"

echo "3. «Заказы» и «Уведомления» — при старте каждый объявляет свои права (и роли)"
if [ -z "${SKIP_BUILD:-}" ]; then
  scripts/package/pack.sh > /dev/null
  "${SHOP[@]}" build shop-orders shop-notifications > /dev/null
fi
"${SHOP[@]}" up -d --no-build --force-recreate shop-orders shop-notifications 2>&1 | grep -iv "^ *\(container\|network\)" || true
for port in 5300 5310; do
  for _ in $(seq 30); do [ "$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:$port/health/live")" = 200 ] && break; sleep 1; done
done
# Права и роли руками не заводим — ждём, пока сервисы объявят их сами.
for _ in $(seq 60); do
  case "$(admin_api GET /auth/admin/apps/orders/roles)" in 200*'"manager"'*) break ;; esac
  sleep 1
done
for _ in $(seq 60); do
  case "$(admin_api GET /auth/admin/services/grantable)" in 200*'notifications:notifications.send'*) break ;; esac
  sleep 1
done
# «Заказам» — право «Уведомлений» присылать уведомления (и читать журнал): их токен по ключу станет адресован и
# «Уведомлениям». Следующий токен «Заказы» возьмут уже с ним.
expect "$(admin_api PUT /auth/admin/services/orders/permissions \
  '{"permissions":["service.catalog.declare","notifications:notifications.send","notifications:notifications.read"]}')" 200 "право «Уведомлений» ключу «Заказов»"
echo "service-orders — права «Уведомлений»: notifications.send, notifications.read"
expect "$(admin_api GET /auth/admin/apps/orders/roles)" 200 "роли «Заказов»"
admin_api GET /auth/admin/apps/orders/roles | python3 -c 'import json,sys
for r in json.loads(sys.stdin.read().split(" ",1)[1])["result"]:
    print(r["name"], "—", ", ".join(r["permissions"]))'

echo "4. Сотрудник и покупатель (пароль $PASSWORD)"
# Учётки — мастер-админкой (в бою люди регистрируются сами: вход по коду). Роль сотрудника — в админке проекта
# (роль manager уже есть: её объявили «Заказы» при старте).
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
# Режим в «Заказах» сотрудник выбирает сам — в личном кабинете (как кнопкой «Режим в приложениях»).
MANAGER_JAR="$WORK/manager"; : > "$MANAGER_JAR"
expect "$(login "$MANAGER_JAR" /account/ "$MANAGER_PHONE")" /account/ "вход сотрудника в кабинет"
expect "$(curl -s -o /dev/null -w '%{http_code}' -b "$MANAGER_JAR" -c "$MANAGER_JAR" -H 'Content-Type: application/json' \
  -d '{"app":"orders","activeMode":"manager"}' "$API/auth/mode")" 200 "режим manager в «Заказах»"
echo "сотрудник +$MANAGER_PHONE (manager@demo-shop.test) — режим manager; покупатель +$BUYER_PHONE (buyer@demo-shop.test)"

cat <<INFO
Готово: магазин — http://localhost:5300 (вход: +$BUYER_PHONE покупатель, +$MANAGER_PHONE сотрудник, пароль $PASSWORD;
новый покупатель — «Войти по коду», код в логе Keycloak). Письма — http://localhost:8025.
INFO
