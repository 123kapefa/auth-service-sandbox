#!/usr/bin/env bash
# Проверка этапа 6а (docs/keycloak/06a-embedding.md): сервис входа как компонент — второй проект на том же Keycloak.
# С нуля, теми же скриптами, что и в бою: проект «Демо-магазин» (realm demo-shop) — create-project.sh, первый админ —
# add-admin.py, приложение «Заказы» — add-app.py; свой экземпляр сервиса из того же образа (infra/projects/demo-shop).
# Проекты изолированы: пользователи и токены одного не действуют в другом.
# Этап 6б (docs/keycloak/06b-account-branding.md): кабинет и оформление в новом проекте — из шаблона; проект, созданный
# раньше, получает их мигратором настроек проектов (scripts/project/migrate-realms.py, docs/migrations.md).
# Запуск: стенд песочницы поднят (Keycloak, Postgres, Redis, сервис на 8080), затем
#   scripts/keycloak/check-new-project.sh
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
API="${DEMO_SHOP_API:-http://localhost:8082}"
SANDBOX_API="${SANDBOX_API:-http://localhost:8080}"
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM=demo-shop
ADMIN_PHONE=79995550001
COMPOSE=(docker compose --project-directory "$ROOT" -f "$ROOT/compose.yaml" -f "$ROOT/infra/projects/demo-shop/compose.yaml")

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

# json <jar> <путь> <python-выражение над r> — «<код> <значение из result>»
json() {
  printf '%s ' "$(curl -s -o "$WORK/r.json" -w '%{http_code}' -b "$1" "$API$2")"
  python3 -c '
import json,sys
d=json.load(open(sys.argv[1])); r=d.get("result")
print(eval(sys.argv[2]) if r is not None else (d.get("error") or {}).get("messages",[{}])[0].get("code",""))' "$WORK/r.json" "$3"
}

echo "0. Проект с нуля: realm, первый админ, приложение, экземпляр сервиса"
MASTER=$(master_token)
curl -s -o /dev/null -X DELETE -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM"
check "проект создан" \
  "$(DEV=1 AUTH_BFF_CLIENT_SECRET=demo-shop-bff-secret "$ROOT/scripts/project/create-project.sh" "$REALM" "Демо-магазин" "$API" http://auth-service-demo-shop:8080 2>/dev/null | head -1)" \
  "Проект «Демо-магазин» создан: realm $REALM, клиенты auth-api и auth-bff."
check "первый админ" "$("$ROOT/scripts/project/add-admin.py" "$REALM" "$ADMIN_PHONE" --email admin@demo-shop.test --password "$PASSWORD" | cut -d' ' -f1-4)" \
  "Админ проекта $REALM: +$ADMIN_PHONE"
check "приложение «Заказы»" \
  "$("$ROOT/scripts/project/add-app.py" "$REALM" orders "Заказы" --default-role customer --web http://localhost:5300 | cut -d: -f1)" \
  "Приложение «Заказы» подключено к $REALM"
# Повторный запуск после сбоя: шаги уже сделаны — пропускаются, атрибуты админа (режим в «Заказах») не теряются.
ADMIN_ID=$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/users?username=$ADMIN_PHONE&exact=true" | python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])')
curl -s -o /dev/null -X PUT -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' \
  -d '{"attributes":{"phone":["+'"$ADMIN_PHONE"'"],"active_mode":["admin"],"active_mode.orders":["customer"]}}' "$KC/admin/realms/$REALM/users/$ADMIN_ID"
check "повторный запуск add-app.py и add-admin.py — без ошибок" \
  "$("$ROOT/scripts/project/add-app.py" "$REALM" orders "Заказы" --default-role customer --web http://localhost:5300 2>&1 | cut -d: -f1) / $("$ROOT/scripts/project/add-admin.py" "$REALM" "$ADMIN_PHONE" --email admin@demo-shop.test --password "$PASSWORD" 2>&1 | cut -d' ' -f1-4)" \
  "Приложение «Заказы» подключено к $REALM / Админ проекта $REALM: +$ADMIN_PHONE"
check "повторный запуск с другим --web: адрес входа «Заказов» обновлён" \
  "$("$ROOT/scripts/project/add-app.py" "$REALM" orders "Заказы" --web http://localhost:5301 > /dev/null 2>&1; curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients?clientId=orders-web" | python3 -c 'import json,sys; c=json.load(sys.stdin)[0]; print(c["redirectUris"][0], c["webOrigins"][0], c["attributes"]["post.logout.redirect.uris"])'; "$ROOT/scripts/project/add-app.py" "$REALM" orders "Заказы" --web http://localhost:5300 > /dev/null 2>&1)" \
  "http://localhost:5301/* http://localhost:5301 http://localhost:5301/*"
check "повторный запуск: атрибуты админа сохранены (режим в «Заказах»)" \
  "$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/users/$ADMIN_ID" | python3 -c 'import json,sys; print(json.load(sys.stdin)["attributes"].get("active_mode.orders"))')" \
  "['customer']"
# Своя база сервиса проекта переживает пересоздание realm — ключи сервисов прошлого прогона в ней устарели.
"${COMPOSE[@]}" exec -T postgres sh -c 'psql -q -U "$POSTGRES_USER" -d auth_demo_shop -c "DELETE FROM auth.service_keys"' > /dev/null 2>&1
REGISTRATION=$(REALM=$REALM "$ROOT/scripts/keycloak/registration-token.sh")
DEMO_SHOP_REGISTRATION_TOKEN=$REGISTRATION "${COMPOSE[@]}" up -d --force-recreate --wait auth-service-demo-shop > /dev/null 2>&1
# --wait дожидается только запуска контейнера (без healthcheck): сервис ещё стартует — ждём готовности до 60 секунд.
for _ in $(seq 30); do [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")" = 200 ] && break; sleep 2; done
check "экземпляр сервиса проекта отвечает" "$(curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")" 200

echo "1. Вход в проект"
JAR="$WORK/login"; : > "$JAR"
curl -s -o /dev/null -D "$WORK/h" -b "$JAR" -c "$JAR" "$API/auth/login?return_to=/me/"
curl -s -o "$WORK/page.html" -b "$JAR" -c "$JAR" "$(header "$WORK/h" location)"
check "страница входа — realm проекта, тема platform, название проекта" \
  "$(kc "$WORK/page.html" 'c["templateName"] + " " + c["url"]["resourcesUrl"].split("/")[-1] + " " + c["realm"]["name"] + " " + c["realm"]["displayName"]')" \
  "login.ftl platform $REALM Демо-магазин"
ADMIN="$WORK/admin"; : > "$ADMIN"
check "админ проекта вошёл" "$(login "$ADMIN" /admin/ "$ADMIN_PHONE")" /admin/
check "«кто я»: админ проекта, приложения — auth-api и «Заказы»" \
  "$(json "$ADMIN" /auth/admin/me 'str("services.manage" in r["permissions"]) + " " + ",".join(sorted(a["clientId"] for a in r["apps"]))')" \
  "200 True auth-api,orders"
check "роли «Заказов»: customer (по умолчанию у всех)" "$(json "$ADMIN" /auth/admin/apps/orders/roles '",".join(x["name"] for x in r)')" "200 customer"

echo "2. Новый покупатель проекта — по коду"
NEW="7998$(printf '%07d' $((RANDOM * 32768 % 10000000 + RANDOM % 1000)))"
CUSTOMER="$WORK/customer"; : > "$CUSTOMER"
check "экран кода" "$(code_start "$CUSTOMER" "$NEW")" "Вам звонят на +$NEW."
check "вход по коду" "$(code_submit "$CUSTOMER" "$(phone_code "$NEW")")" /me/
check "покупатель проекта: роль customer" "$(curl -s -b "$CUSTOMER" "$API/me/" | python3 -c 'import json,sys; print(",".join(json.load(sys.stdin)["result"]["roles"]))')" customer

echo "3. Ключ сервиса в проекте"
R=$(curl -s -b "$ADMIN" -H 'Content-Type: application/json' -d '{"code":"notifications","name":"Уведомления","permissions":["service.users.read"]}' "$API/auth/admin/services")
SECRET=$(echo "$R" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["clientSecret"])' 2>/dev/null)
check "ключ выдан" "$([ -n "$SECRET" ] && echo да || echo нет)" да
ST=$(curl -s -d grant_type=client_credentials -d client_id=service-notifications --data-urlencode "client_secret=$SECRET" "$KC/realms/$REALM/protocol/openid-connect/token" |
  python3 -c 'import json,sys; print(json.load(sys.stdin).get("access_token",""))')
check "сервис по ключу читает пользователя проекта" \
  "$(curl -s -H "Authorization: Bearer $ST" "$API/auth/services/users?login=$NEW" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["phone"])')" "+$NEW"

echo "4. Изоляция проектов"
check "токен сервиса проекта не действует в песочнице — 401" \
  "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $ST" "$SANDBOX_API/auth/services/users?login=$NEW")" 401
SANDBOX_USER=$(curl -s -d grant_type=password -d client_id=dev-cli -d username=71234567890 -d "password=$PASSWORD" "$KC/realms/sandbox/protocol/openid-connect/token" |
  python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')
check "токен админа песочницы не действует в проекте — 401" \
  "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $SANDBOX_USER" "$API/auth/admin/me")" 401
OTHER="$WORK/other"; : > "$OTHER"
check "пользователь песочницы в проекте не входит" "$(starts "$(login "$OTHER" /me/ 71234567890)" "keycloak:")" "keycloak:"
check "покупателя проекта нет в песочнице" \
  "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $MASTER" "$KC/admin/realms/sandbox/users?username=$NEW&exact=true" ; curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/sandbox/users?username=$NEW&exact=true" | python3 -c 'import json,sys; print(" найден" if json.load(sys.stdin) else " нет")')" \
  "200 нет"


echo "5. Кабинет и оформление (этап 6б) — из шаблона"
# kc_get <путь Admin API> <python-выражение над r>
kc_get() { curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM$1" | python3 -c "import json,sys; r=json.load(sys.stdin); print($2)"; }
BFF_ID=$(kc_get "/clients?clientId=auth-bff" 'r[0]["id"]')
API_ID=$(kc_get "/clients?clientId=auth-api" 'r[0]["id"]')
SA_ID=$(kc_get "/clients/$BFF_ID/service-account-user" 'r["id"]')
project_6b() {
  echo "смена почты: $(kc_get /authentication/required-actions/UPDATE_EMAIL 'r["enabled"], r["config"].get("verifyEmail")')" \
    "· возврат: $(kc_get "/clients/$BFF_ID" 'r.get("baseUrl")')" \
    "· учётка сервиса: $(kc_get "/users/$SA_ID/role-mappings/realm" '"manage-branding" in [x["name"] for x in r]')" \
    "· admin: $(kc_get "/clients/$API_ID/roles/admin/composites" '"branding.manage" in [x["name"] for x in r]')"
}
PROJECT_6B="смена почты: True true · возврат: $API/auth/account · учётка сервиса: True · admin: True"
check "в realm: смена почты после подтверждения, «вернуться» — в кабинет, оформление — служебной учётке и admin" "$(project_6b)" "$PROJECT_6B"
LATEST=$(ls "$ROOT"/scripts/project/realm-migrations/[0-9][0-9][0-9][0-9]-*.py | wc -l | tr -d ' ')
check "версия настроек — последняя миграция, мигратору делать нечего" \
  "$(kc_get "" 'r["attributes"].get("platform.realmVersion")') / $("$ROOT/scripts/project/migrate-realms.py" "$REALM" | sed -n 1p)" \
  "$LATEST / $REALM: версия $LATEST, изменений нет"
check "/auth/account — в кабинет проекта" "$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' "$API/auth/account")" "302 $API/account/"
check "кабинет покупателя: профиль" "$(json "$CUSTOMER" /me/profile 'r["phone"]')" "200 +$NEW"
check "админ проекта сохранил оформление" \
  "$(curl -s -o /dev/null -w '%{http_code}' -b "$ADMIN" -X PUT -H 'Content-Type: application/json' -d '{"name":"Демо-магазин","color":"#0A7D4F","theme":"system"}' "$API/auth/admin/branding")" 200
check "оформление — на странице входа (плагин) и в кабинете покупателя" \
  "$(curl -s "$KC/realms/$REALM/platform-branding" | python3 -c 'import json,sys; r=json.load(sys.stdin); print(r["name"], r["color"])') / $(json "$CUSTOMER" /me/branding 'r["name"] + " " + r["color"]')" \
  "Демо-магазин #0a7d4f / 200 Демо-магазин #0a7d4f"

echo "6. Проект, созданный до 6б: мигратор настроек проектов"
# Как в проекте из шаблона до 6б: смена почты сразу, без письма; «вернуться» — никуда; оформление менять некому.
kc_put() { curl -s -o /dev/null -X "$1" -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' -d "$3" "$KC/admin/realms/$REALM$2"; }
kc_put PUT /authentication/required-actions/UPDATE_EMAIL \
  "$(kc_get /authentication/required-actions/UPDATE_EMAIL 'json.dumps({**r, "enabled": False, "config": {}})')"
kc_put PUT "/clients/$BFF_ID" "$(kc_get "/clients/$BFF_ID" 'json.dumps({**r, "baseUrl": ""})')"
kc_put DELETE "/clients/$API_ID/roles/admin/composites" "[$(kc_get "/clients/$API_ID/roles/branding.manage" 'json.dumps(r)')]"
kc_put DELETE /roles/manage-branding ""
# Проект старше миграций — без версии.
kc_put PUT "" "$(kc_get "" 'json.dumps({"attributes": {k: v for k, v in r["attributes"].items() if k != "platform.realmVersion"}})')"
check "до обновления: ничего из 6б" "$(project_6b)" "смена почты: False None · возврат:  · учётка сервиса: False · admin: False"
OLD="$WORK/old-admin"; : > "$OLD"
login "$OLD" /admin/ "$ADMIN_PHONE" > /dev/null
check "до обновления: оформление админу проекта не доступно" "$(curl -s -o /dev/null -w '%{http_code}' -b "$OLD" "$API/auth/admin/branding")" 403
check "до обновления: версии нет" "$("$ROOT/scripts/project/migrate-realms.py" --status "$REALM")" "$REALM: версия 0 из $LATEST"
check "мигратор применил все миграции" "$("$ROOT/scripts/project/migrate-realms.py" "$REALM" | sed -n 1p)" "$REALM: версия 0 → $LATEST"
check "повторный запуск — ничего не меняет" "$("$ROOT/scripts/project/migrate-realms.py" "$REALM" 2>&1 | sed -n 1p)" "$REALM: версия $LATEST, изменений нет"
check "после обновления — как из шаблона" "$(project_6b)" "$PROJECT_6B"
UPGRADED="$WORK/upgraded-admin"; : > "$UPGRADED"
login "$UPGRADED" /admin/ "$ADMIN_PHONE" > /dev/null
check "админ вошёл заново — оформление доступно" "$(json "$UPGRADED" /auth/admin/branding 'r["name"]')" "200 Демо-магазин"
# Плагин проверяет роль учётки в базе Keycloak, а не в токене: сервис сохраняет сразу, без перезапуска.
check "и сохраняется — сервис не перезапускали" \
  "$(curl -s -o /dev/null -w '%{http_code}' -b "$UPGRADED" -X PUT -H 'Content-Type: application/json' -d '{"name":"Демо-магазин","theme":"system"}' "$API/auth/admin/branding")" 200

summary
