#!/usr/bin/env bash
# Проверка этапа 6а.4 (docs/keycloak/06a-embedding.md#магазин-заказы-и-уведомления): новый проект на сервисе входа —
# магазин из двух мини-сервисов (samples/shop). Покупатель входит через сервис входа проекта и приходит в «Заказы» со
# своими правами; «Заказы» зовут «Уведомления» по своему ключу с правом «Уведомлений», «Уведомления» узнают у сервиса
# входа, куда писать; чужие запросы отклоняются; перевыпущенный ключ подхватывается из файла без перезапуска, отозванный
# перестаёт действовать сразу; права и роли сервисы объявляют из своего кода; режим в «Заказах» человек меняет сам в
# личном кабинете (6а.6).
# Запуск: стенд песочницы поднят (docker compose up -d --build --wait), затем
#   scripts/keycloak/check-shop.sh
# Сначала поднимает магазин с нуля (samples/shop/setup.sh; SKIP_SETUP=1 — магазин уже поднят, SKIP_BUILD=1 — образы
# магазина уже собраны).
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
export API="${DEMO_SHOP_API:-http://localhost:8082}"
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
SHOP="${SHOP:-http://localhost:5300}"
NOTIFY="${NOTIFY:-http://localhost:5310}"
MAIL="${MAIL:-http://localhost:${MAILPIT_UI_PORT:-8025}}"
REALM=demo-shop
TOKEN_URL="$KC/realms/$REALM/protocol/openid-connect/token"
SHOP_COMPOSE=(docker compose --project-directory "$ROOT" -f "$ROOT/compose.yaml" -f "$ROOT/infra/projects/demo-shop/compose.yaml"
  -f "$ROOT/samples/shop/compose.yaml")

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
cd "$ROOT" || exit 1

if [ -z "${SKIP_SETUP:-}" ]; then
  echo "0. Магазин с нуля: samples/shop/setup.sh"
  samples/shop/setup.sh > "$WORK/setup.log" 2>&1
  check "магазин поднят" "$? $(tail -2 "$WORK/setup.log" | head -1 | cut -d' ' -f1-4)" "0 Готово: магазин — http://localhost:5300"
fi
SECRETS="$ROOT/samples/shop/.secrets"
key_secret() { cat "$SECRETS/$1.key"; }

# --- Вход в «Заказы»: как страница магазина — Authorization Code + PKCE, клиент orders-web ---------------------------
# shop_login_page <jar> — страница входа сервиса входа проекта для «Заказов»; verifier — в $WORK/verifier
shop_login_page() {
  local pair
  pair=$(python3 -c 'import secrets,hashlib,base64; v=secrets.token_urlsafe(48); print(v, base64.urlsafe_b64encode(hashlib.sha256(v.encode()).digest()).rstrip(b"=").decode())')
  echo "${pair% *}" > "$WORK/verifier"
  curl -s -o "$WORK/page.html" -b "$1" -c "$1" -G "$KC/realms/$REALM/protocol/openid-connect/auth" \
    --data-urlencode client_id=orders-web --data-urlencode response_type=code --data-urlencode scope=openid \
    --data-urlencode "redirect_uri=$SHOP/" --data-urlencode state=check --data-urlencode "code_challenge=${pair#* }" \
    --data-urlencode code_challenge_method=S256 --data-urlencode platform_app=orders
}
# shop_tokens <адрес возврата> — код → токены (запрос — как из браузера страницы); печатает access-токен
shop_tokens() {
  local code
  code=$(python3 -c 'import sys; from urllib.parse import urlparse, parse_qs; print(parse_qs(urlparse(sys.argv[1]).query).get("code", [""])[0])' "$1")
  curl -s -H "Origin: $SHOP" -d grant_type=authorization_code -d client_id=orders-web -d "code=$code" \
    --data-urlencode "redirect_uri=$SHOP/" --data-urlencode "code_verifier=$(cat "$WORK/verifier")" "$TOKEN_URL" |
    python3 -c 'import json,sys; print(json.load(sys.stdin).get("access_token", ""))'
}
# shop_login <логин> — вход паролем; печатает access-токен «Заказов»
shop_login() {
  local jar="$WORK/jar-$1"; : > "$jar"
  shop_login_page "$jar"
  curl -s -o "$WORK/page.html" -D "$WORK/h" -b "$jar" -c "$jar" --data-urlencode "username=$1" \
    --data-urlencode "password=$PASSWORD" "$(form_action "$WORK/page.html")"
  shop_tokens "$(header "$WORK/h" location)"
}
# shop_code_login <телефон> — новый покупатель: «Войти по коду», код звонка (заглушка телефонии), «Не сейчас» на
# предложении ключа доступа; печатает access-токен
shop_code_login() {
  local jar="$WORK/jar-$1"; : > "$jar"
  shop_login_page "$jar"
  curl -s -o "$WORK/page.html" -b "$jar" -c "$jar" --data-urlencode "username=$1" --data-urlencode "codeLogin=" \
    "$(form_action "$WORK/page.html")"
  curl -s -o "$WORK/page.html" -D "$WORK/h" -b "$jar" -c "$jar" --data-urlencode "code=$(phone_code "$1")" \
    "$(form_action "$WORK/page.html")"
  skip_passkey_offer "$jar" "$WORK/page.html" "$WORK/h"
  shop_tokens "$(header "$WORK/h" location)"
}
# call <токен> <метод> <адрес> [тело] — печатает «<код> <JSON ответа>»
call() {
  local args=(-s -o "$WORK/r.json" -w '%{http_code}' -X "$2")
  [ -n "$1" ] && args+=(-H "Authorization: Bearer $1")
  [ -n "${4:-}" ] && args+=(-H 'Content-Type: application/json' -d "$4")
  printf '%s ' "$(curl "${args[@]}" "$3")"
  cat "$WORK/r.json"
}
# get <ответ call> <python-выражение над r> — «<код> <значение>»
get() { python3 -c 'import json,sys; c,_,b=sys.argv[1].partition(" "); r=json.loads(b) if b.strip() else None; print(c, eval(sys.argv[2]))' "$1" "$2"; }
code() { echo "${1%% *}"; }
claims() { python3 -c 'import json,sys,base64; p=sys.argv[1].split(".")[1]; p+="="*(-len(p)%4); c=json.loads(base64.urlsafe_b64decode(p)); print(eval(sys.argv[2]))' "$1" "$2"; }
service_token() {
  curl -s -d grant_type=client_credentials -d "client_id=$1" --data-urlencode "client_secret=$2" "$TOKEN_URL" |
    python3 -c 'import json,sys; print(json.load(sys.stdin).get("access_token", ""))'
}
mail_subject() {
  for _ in 1 2 3 4 5; do
    curl -s "$MAIL/api/v1/search?query=$(python3 -c 'import sys,urllib.parse; print(urllib.parse.quote(sys.argv[1]))' "to:$1 subject:\"$2\"")" |
      python3 -c 'import json,sys; m=json.load(sys.stdin)["messages"]; print(m[0]["Subject"] if m else "")' | grep . && return
    sleep 1
  done
}

echo "1. Вход в «Заказы» через сервис входа проекта"
shop_login_page "$WORK/page-jar"
check "страница входа — проекта (realm, тема platform), вход приложения «Заказы»" \
  "$(kc "$WORK/page.html" 'c["templateName"] + " " + c["realm"]["name"] + " " + c["url"]["resourcesUrl"].split("/")[-1] + " " + c["client"]["clientId"]')" \
  "login.ftl $REALM platform orders-web"
check "токен страница берёт из браузера: CORS для адреса магазина" \
  "$(curl -s -D - -o /dev/null -X OPTIONS -H "Origin: $SHOP" -H 'Access-Control-Request-Method: POST' "$TOKEN_URL" | header /dev/stdin access-control-allow-origin)" "$SHOP"
NEW="7997$(printf '%07d' $((RANDOM * 32768 % 10000000 + RANDOM % 1000)))"
CUSTOMER=$(shop_code_login "$NEW")
check "новый покупатель вошёл по коду: токен «Заказов» (aud orders)" "$(claims "$CUSTOMER" 'c["aud"], c["azp"]')" "('orders', 'orders-web')"
check "права покупателя — роль по умолчанию customer, хотя режим в «Заказах» он не выбирал" \
  "$(get "$(call "$CUSTOMER" GET "$SHOP/api/me")" 'r["mode"], ",".join(r["permissions"]), r["phone"]')" \
  "200 ('customer', 'orders.create,orders.view', '+$NEW')"

echo "2. Заказ: «Заказы» зовут «Уведомления» по своему ключу"
R=$(call "$CUSTOMER" POST "$SHOP/api/orders" '{"item":"Кофе"}')
NUMBER=$(get "$R" 'r["number"]' | cut -d' ' -f2)
check "заказ принят, уведомление — SMS покупателю (почты у него нет)" "$(get "$R" 'r["status"], r["notification"]')" \
  "201 ('принят', 'SMS на +$NEW')"
check "«SMS» ушла (лог «Уведомлений»)" \
  "$("${SHOP_COMPOSE[@]}" logs shop-notifications --since 2m 2>/dev/null | grep -c "\[DEV SMS\] +$NEW: Заказ №$NUMBER принят: Кофе")" 1
ORDERS_KEY=$(service_token service-orders "$(key_secret orders)")
CUSTOMER_ID=$(claims "$CUSTOMER" 'c["sub"]')
check "токен ключа «Заказов» адресован сервису входа и «Уведомлениям», права — по приложениям" \
  "$(claims "$ORDERS_KEY" 'sorted(c["aud"]), c["permissions"], c["app_permissions"]')" \
  "(['auth-api', 'notifications'], ['service.catalog.declare'], {'notifications': ['notifications.read', 'notifications.send']})"
check "в журнале «Уведомлений»: прислали «Заказы» по ключу service-orders" \
  "$(get "$(call "$ORDERS_KEY" GET "$NOTIFY/api/notifications?userId=$CUSTOMER_ID")" 'len(r), r[0]["sender"], r[0]["text"]')" \
  "200 (1, 'service-orders', 'Заказ №$NUMBER принят: Кофе')"
BUYER=$(shop_login 79995550003)
R=$(call "$BUYER" POST "$SHOP/api/orders" '{"item":"Чайник"}')
BUYER_ORDER=$(get "$R" 'r["number"]' | cut -d' ' -f2)
check "покупатель с почтой — уведомление письмом" "$(get "$R" 'r["notification"]')" "201 письмо на buyer@demo-shop.test"
check "письмо дошло (Mailpit)" "$(mail_subject buyer@demo-shop.test "Заказ №$BUYER_ORDER принят")" "Заказ №$BUYER_ORDER принят: Чайник"

echo "3. Права пользователя в «Заказах»"
check "покупатель видит только свои заказы" "$(get "$(call "$CUSTOMER" GET "$SHOP/api/orders")" '[o["number"] for o in r]')" "200 [$NUMBER]"
check "все заказы покупателю — 403" "$(code "$(call "$CUSTOMER" GET "$SHOP/api/orders/all")")" 403
check "статус заказа покупателю менять нельзя — 403" "$(code "$(call "$CUSTOMER" POST "$SHOP/api/orders/$NUMBER/status" '{"status":"отправлен"}')")" 403
MANAGER=$(shop_login 79995550002)
check "сотрудник: режим manager, права manager" "$(get "$(call "$MANAGER" GET "$SHOP/api/me")" 'r["mode"], ",".join(r["permissions"])')" \
  "200 ('manager', 'orders.manage,orders.view')"
check "сотрудник видит все заказы" "$(get "$(call "$MANAGER" GET "$SHOP/api/orders/all")" 'sorted(o["number"] for o in r)[-2:]')" \
  "200 [$NUMBER, $BUYER_ORDER]"
R=$(call "$MANAGER" POST "$SHOP/api/orders/$BUYER_ORDER/status" '{"status":"отправлен"}')
check "сотрудник сменил статус — покупателю письмо" "$(get "$R" 'r["status"], r["notification"]')" \
  "200 ('отправлен', 'письмо на buyer@demo-shop.test')"
check "письмо о статусе дошло" "$(mail_subject buyer@demo-shop.test "Заказ №$BUYER_ORDER (Чайник): отправлен")" "Заказ №$BUYER_ORDER (Чайник): отправлен"
check "заказать сотруднику нельзя (в режиме manager нет orders.create) — 403" \
  "$(code "$(call "$MANAGER" POST "$SHOP/api/orders" '{"item":"Кофе"}')")" 403

echo "4. Чужие запросы отклонены"
PROJECT_USER=$(curl -s -d grant_type=password -d client_id=dev-cli -d username=79995550003 -d "password=$PASSWORD" "$TOKEN_URL" |
  python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')
SANDBOX_USER=$(curl -s -d grant_type=password -d client_id=dev-cli -d username=79990000001 -d "password=$PASSWORD" \
  "$KC/realms/sandbox/protocol/openid-connect/token" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')
NOTIFICATIONS_KEY=$(service_token service-notifications "$(key_secret notifications)")
check "«Заказы» без токена — 401" "$(code "$(call "" GET "$SHOP/api/orders")")" 401
check "«Заказы» с токеном сервиса входа проекта (не «Заказам», aud auth-api) — 401" "$(code "$(call "$PROJECT_USER" GET "$SHOP/api/orders")")" 401
check "«Заказы» с токеном другого проекта (песочница) — 401" "$(code "$(call "$SANDBOX_USER" GET "$SHOP/api/orders")")" 401
check "«Заказы» с токеном сервиса по ключу — 401" "$(code "$(call "$ORDERS_KEY" GET "$SHOP/api/orders")")" 401
BODY="{\"userId\":\"$CUSTOMER_ID\",\"text\":\"чужое\"}"
check "«Уведомления» без токена — 401" "$(code "$(call "" POST "$NOTIFY/api/notifications" "$BODY")")" 401
check "«Уведомления» с токеном человека из сервиса входа проекта (aud auth-api) — 401" \
  "$(code "$(call "$PROJECT_USER" POST "$NOTIFY/api/notifications" "$BODY")")" 401
check "«Уведомления» с токеном покупателя «Заказов» — 401" "$(code "$(call "$CUSTOMER" POST "$NOTIFY/api/notifications" "$BODY")")" 401
check "«Уведомления» с ключом без их прав (service-notifications, aud только auth-api) — 401" \
  "$(code "$(call "$NOTIFICATIONS_KEY" POST "$NOTIFY/api/notifications" "$BODY")")" 401
check "«Уведомления» с токеном другого проекта — 401" "$(code "$(call "$SANDBOX_USER" POST "$NOTIFY/api/notifications" "$BODY")")" 401
check "в журнале «Уведомлений» чужого нет" "$(get "$(call "$ORDERS_KEY" GET "$NOTIFY/api/notifications?userId=$CUSTOMER_ID")" 'len(r)')" "200 1"
check "ключ «Заказов» читать пользователей сервиса входа не может (права не выдано) — 403" \
  "$(code "$(call "$ORDERS_KEY" GET "$API/auth/services/users/$CUSTOMER_ID")")" 403
check "ключ «Уведомлений» — может" "$(get "$(call "$NOTIFICATIONS_KEY" GET "$API/auth/services/users/$CUSTOMER_ID")" 'r["result"]["phone"]')" "200 +$NEW"

echo "5. Перевыпуск и отзыв ключа"
ADMIN="$WORK/admin"; : > "$ADMIN"
login "$ADMIN" /admin/ 79995550001 > /dev/null
# admin <метод> <путь> [тело] — API админки проекта от имени админа; печатает «<код> <JSON ответа>»
admin() {
  local args=(-s -o "$WORK/r.json" -w '%{http_code}' -b "$ADMIN" -c "$ADMIN" -X "$1")
  [ -n "${3:-}" ] && args+=(-H 'Content-Type: application/json' -d "$3")
  printf '%s ' "$(curl "${args[@]}" "$API$2")"
  cat "$WORK/r.json"
}
# save_orders_secret <ответ на перевыпуск> — новый секрет в файл «Заказов» (на месте: контейнер видит его сразу)
save_orders_secret() {
  get "$1" 'r["result"]["clientSecret"]' | cut -d' ' -f2 | tr -d '\n' > "$SECRETS/orders.key"
}
order_notification() { get "$(call "$CUSTOMER" POST "$SHOP/api/orders" "{\"item\":\"$1\"}")" 'r["notification"]'; }
R=$(admin POST /auth/admin/services/orders/rotate)
sleep 2 # отметки отзыва сервис держит в памяти до 2 с (TokenRevocationOptions.LocalCache)
check "ключ «Заказов» перевыпущен срочно (мог утечь)" "$(code "$R") $([ -n "$(get "$R" 'r["result"]["clientSecret"]')" ] && echo 'новый секрет')" "200 новый секрет"
check "старый секрет и его токены не принимаются сразу: заказ принят, уведомление не ушло" "$(order_notification Сахар)" \
  "201 не отправлено: сервис входа не принял ключ «Заказов»"
save_orders_secret "$R"
check "новый секрет — в файл, без перезапуска «Заказов»: уведомления снова идут" "$(order_notification Соль)" "201 SMS на +$NEW"
R=$(admin POST /auth/admin/services/orders/rotate '{"keepTokens":true}')
check "плановый перевыпуск (keepTokens): выданный токен доживает срок — уведомления идут и со старым секретом в файле" \
  "$(code "$R") $(order_notification Перец)" "200 201 SMS на +$NEW"
check "старым секретом новый токен уже не взять" "$(service_token service-orders "$(key_secret orders)" | sed 's/^$/нет/')" нет
save_orders_secret "$R"
check "новый секрет в файле — новый токен по нему выдаётся" "$(service_token service-orders "$(key_secret orders)" | cut -c1-2)" ey

admin DELETE /auth/admin/services/check-stranger > /dev/null
R=$(admin POST /auth/admin/services '{"code":"check-stranger","name":"Чужой сервис","permissions":["service.users.read"]}')
STRANGER_SECRET=$(get "$R" 'r["result"]["clientSecret"]' | cut -d' ' -f2)
STRANGER=$(service_token service-check-stranger "$STRANGER_SECRET")
check "ещё один ключ проекта: пользователей читает" "$(code "$(call "$STRANGER" GET "$API/auth/services/users/$CUSTOMER_ID")")" 200
check "но в «Уведомления» не пройдёт: их прав у ключа нет (aud без notifications) — 401" \
  "$(code "$(call "$STRANGER" POST "$NOTIFY/api/notifications" "$BODY")")" 401
check "права в приложении, которого нет, ключу не выдать — 400" \
  "$(code "$(admin PUT /auth/admin/services/check-stranger/permissions '{"permissions":["notifications:notifications.nope"]}')")" 400
check "выдали право «Уведомлений» — пускают (новый токен)" \
  "$(code "$(admin PUT /auth/admin/services/check-stranger/permissions '{"permissions":["service.users.read","notifications:notifications.send"]}')") $(code "$(call "$(service_token service-check-stranger "$STRANGER_SECRET")" POST "$NOTIFY/api/notifications" "$BODY")")" \
  "200 200"
check "сняли — прежний токен не действует сразу (401), читать пользователей тоже больше нельзя" \
  "$(code "$(admin PUT /auth/admin/services/check-stranger/permissions '{"permissions":[]}')") $(code "$(call "$STRANGER" GET "$API/auth/services/users/$CUSTOMER_ID")")" \
  "200 401"
admin PUT /auth/admin/services/check-stranger/permissions '{"permissions":["service.users.read"]}' > /dev/null
sleep 1 # отметка отзыва включает свою секунду — токен той же секунды тоже был бы отклонён
STRANGER=$(service_token service-check-stranger "$STRANGER_SECRET")
check "ключ отозван" "$(code "$(admin DELETE /auth/admin/services/check-stranger)")" 200
check "токен отозванного ключа не действует сразу — 401" "$(code "$(call "$STRANGER" GET "$API/auth/services/users/$CUSTOMER_ID")")" 401
check "и нового токена по нему не дают" "$(service_token service-check-stranger x | sed 's/^$/нет/')" нет

echo "6. Права и роли «Заказов» — из их кода (AddAppCatalog), не из админки"
check "права «Заказов» заведены кодом (source=code)" \
  "$(get "$(admin GET /auth/admin/apps/orders/permissions)" '",".join(sorted(p["name"] + ":" + str(p["source"]) for p in r["result"]))')" \
  "200 orders.create:code,orders.manage:code,orders.view:code"
ROLES='" ".join(sorted(x["name"] + "=" + ",".join(sorted(x["permissions"])) for x in r["result"]))'
check "роли из кода: customer и manager со своими правами" "$(get "$(admin GET /auth/admin/apps/orders/roles)" "$ROLES")" \
  "200 customer=orders.create,orders.view manager=orders.manage,orders.view"
check "право, которое проверяет код, в админке не удалить — 409" "$(code "$(admin DELETE /auth/admin/apps/orders/permissions/orders.view)")" 409
CATALOG='{"permissions":[{"name":"orders.view","description":"x"}],"roles":[]}'
admin DELETE /auth/admin/services/check-reader > /dev/null
R=$(admin POST /auth/admin/services '{"code":"check-reader","name":"Читатель","permissions":["service.users.read"]}')
ORDERS_ONLY_READ=$(service_token service-check-reader "$(get "$R" 'r["result"]["clientSecret"]' | cut -d' ' -f2)")
check "ключ без права объявлять каталог — 403" \
  "$(code "$(call "$ORDERS_ONLY_READ" PUT "$API/auth/services/catalog" "$CATALOG")")" 403
check "права «Уведомлений» — тоже из их кода: только права, ролей для людей нет" \
  "$(get "$(admin GET /auth/admin/services/grantable)" '[(a["app"], [p["name"] for p in a["permissions"]]) for a in r["result"]]')" \
  "200 [('notifications', ['notifications.read', 'notifications.send']), ('orders', ['orders.create', 'orders.manage', 'orders.view'])]"
# Админ поменял состав роли — перезапуск «Заказов» (каталог объявляется заново) его правку не откатывает.
check "админ добавил сотруднику право «оформить заказ»" \
  "$(code "$(admin PUT /auth/admin/apps/orders/roles/manager '{"permissions":["orders.view","orders.manage","orders.create"]}')")" 200
SINCE=$(date -u +%Y-%m-%dT%H:%M:%SZ)
"${SHOP_COMPOSE[@]}" restart shop-orders > /dev/null 2>&1
for _ in $(seq 30); do "${SHOP_COMPOSE[@]}" logs --since "$SINCE" shop-orders 2>/dev/null | grep -q "App catalog of orders declared" && break; sleep 1; done
check "«Заказы» перезапущены и снова объявили каталог" \
  "$("${SHOP_COMPOSE[@]}" logs --since "$SINCE" shop-orders 2>/dev/null | grep -o "App catalog of orders declared: new permissions \[\]" | head -1)" \
  "App catalog of orders declared: new permissions []"
check "правка админа осталась" "$(get "$(admin GET /auth/admin/apps/orders/roles)" "$ROLES")" \
  "200 customer=orders.create,orders.view manager=orders.create,orders.manage,orders.view"
admin PUT /auth/admin/apps/orders/roles/manager '{"permissions":["orders.view","orders.manage"]}' > /dev/null
admin DELETE /auth/admin/services/check-reader > /dev/null

echo "7. Режим в «Заказах» — сам, в личном кабинете (POST /auth/mode с app)"
CABINET="$WORK/cabinet"; : > "$CABINET"
login "$CABINET" /account/ 79995550002 > /dev/null
# cabinet <метод> <путь> [тело] — API сервиса входа проекта из личного кабинета сотрудника
cabinet() {
  local args=(-s -o "$WORK/r.json" -w '%{http_code}' -b "$CABINET" -c "$CABINET" -X "$1")
  [ -n "${3:-}" ] && args+=(-H 'Content-Type: application/json' -d "$3")
  printf '%s ' "$(curl "${args[@]}" "$API$2")"
  cat "$WORK/r.json"
}
check "кабинет: режимы сотрудника в «Заказах» — customer и manager, выбран manager" \
  "$(get "$(cabinet GET /auth/modes)" '[(a["app"], a["modes"], a["activeMode"]) for a in r["result"] if a["app"] == "orders"]')" \
  "200 [('orders', ['customer', 'manager'], 'manager')]"
check "режима, которого нет, не выбрать — 403" "$(code "$(cabinet POST /auth/mode '{"app":"orders","activeMode":"admin"}')")" 403
check "переключил «Заказы» на customer" "$(get "$(cabinet POST /auth/mode '{"app":"orders","activeMode":"customer"}')" 'r["result"]["activeMode"]')" "200 customer"
R=$(call "$MANAGER" GET "$SHOP/api/me")
check "старый токен «Заказов» сразу не принимается — обновить (access_changed)" \
  "$(code "$R") $(grep -o access_changed "$WORK/r.json" | head -1)" "401 access_changed"
MANAGER=$(shop_login 79995550002)
check "новый токен «Заказов» — режим customer и его права" "$(get "$(call "$MANAGER" GET "$SHOP/api/me")" 'r["mode"], ",".join(r["permissions"])')" \
  "200 ('customer', 'orders.create,orders.view')"
cabinet POST /auth/mode '{"app":"orders","activeMode":"manager"}' > /dev/null
check "и обратно на manager" "$(get "$(call "$(shop_login 79995550002)" GET "$SHOP/api/me")" 'r["mode"]')" "200 manager"

echo "8. Модуль входа для страницы приложения"
check "сервис входа отдаёт /sdk/platform-login.js" \
  "$(curl -s -o "$WORK/sdk.js" -w '%{http_code} %{content_type}' "$API/sdk/platform-login.js") $(grep -c 'PlatformLogin = { create: create }' "$WORK/sdk.js")" \
  "200 text/javascript 1"
check "страница «Заказов» входит через него" "$(curl -s "$SHOP/" | grep -c "PlatformLogin.create")" 1

summary
