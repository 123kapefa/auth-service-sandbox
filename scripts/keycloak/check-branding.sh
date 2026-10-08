#!/usr/bin/env bash
# Проверка шага 6б.4 (docs/keycloak/06b-account-branding.md): оформление проекта — название, логотип, цвет и тема страниц
# входа. Хранится в Keycloak (displayName и атрибуты realm), меняется из админки через сервис входа и плагин
# platform-branding, страница входа читает его при открытии — без пересборки темы. В конце оформление возвращается.
# Запуск: стенд поднят (Docker: сервис :8080, Keycloak :8180), затем
#   scripts/keycloak/check-branding.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM=sandbox
PLUGIN="$KC/realms/$REALM/platform-branding"
# Картинка 1×1 PNG.
PNG='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='

MASTER=$(master_token)
# api <jar> <метод> <путь> [тело или @файл] — печатает «<код> <код ошибки или result>»
api() {
  curl -s -o "$WORK/r.json" -w '%{http_code}' -b "$1" -X "$2" -H 'Content-Type: application/json' ${4:+-d "$4"} "$API$3"
  printf ' '
  python3 -c '
import json,sys
try: d=json.load(open(sys.argv[1]))
except Exception: print("—"); sys.exit()
print(d["error"]["messages"][0]["code"] if d.get("error") else json.dumps(d["result"], ensure_ascii=False, sort_keys=True))' "$WORK/r.json"
}
plugin() { curl -s "$PLUGIN" | python3 -c "import json,sys; r=json.load(sys.stdin); print($1)"; }
user_token() {
  curl -s -d grant_type=password -d client_id=dev-cli -d "username=$1" -d "password=$PASSWORD" \
    "$KC/realms/$REALM/protocol/openid-connect/token" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])'
}

require_service
ADMIN="$WORK/admin"; : > "$ADMIN"
MANAGER="$WORK/manager"; : > "$MANAGER"
login "$ADMIN" /admin/ 1234567890 > /dev/null
login "$MANAGER" /admin/ 0987654321 > /dev/null
ORIGINAL=$(curl -s -b "$ADMIN" "$API/auth/admin/branding" | python3 -c 'import json,sys; print(json.dumps(json.load(sys.stdin)["result"]))')
trap 'curl -s -o /dev/null -b "$ADMIN" -X PUT -H "Content-Type: application/json" -d "$ORIGINAL" "$API/auth/admin/branding"; rm -rf "$WORK"' EXIT

echo "1. Плагин Keycloak: читать — всем, менять — только учётке с ролью manage-branding"
check "оформление читается без входа (его читает страница входа)" "$(curl -s -o /dev/null -w '%{http_code} %header{cache-control}' "$PLUGIN")" "200 no-cache"
check "без токена не поменять" "$(curl -s -o /dev/null -w '%{http_code}' -X PUT -H 'Content-Type: application/json' -d '{"name":"x","theme":"dark"}' "$PLUGIN")" 401
check "токеном пользователя, даже админа проекта, — нет" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X PUT -H "Authorization: Bearer $(user_token 71234567890)" -H 'Content-Type: application/json' -d '{"name":"x","theme":"dark"}' "$PLUGIN")" 403
check "токеном мастер-админки (другой realm) — нет" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X PUT -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' -d '{"name":"x","theme":"dark"}' "$PLUGIN")" 401
# Есть ли у неё и другие роли realm (manage-sign-in с этапа 10), здесь не важно.
check "у служебной учётки сервиса — роль manage-branding, а не manage-realm" \
  "$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/users?username=service-account-sandbox-bff&exact=true" \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])' \
    | xargs -I{} curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/users/{}/role-mappings" \
    | python3 -c 'import json,sys; r=json.load(sys.stdin); print("manage-branding" in [x["name"] for x in r.get("realmMappings",[])], "manage-realm" in [x["name"] for x in r["clientMappings"]["realm-management"]["mappings"]])')" \
  "True False"

echo "2. Админка: оформление — только админ проекта"
check "админ проекта читает" "$(api "$ADMIN" GET /auth/admin/branding | cut -d' ' -f1)" 200
check "менеджеру — нет" "$(api "$MANAGER" GET /auth/admin/branding | cut -d' ' -f1)" 403
check "в «кто я» у админа — право branding.manage" \
  "$(curl -s -b "$ADMIN" "$API/auth/admin/me" | python3 -c 'import json,sys; print("branding.manage" in json.load(sys.stdin)["result"]["permissions"])')" True

echo "3. Сохранить: название, логотип, цвет, тема"
check "сохранено (цвет — строчными)" \
  "$(api "$ADMIN" PUT /auth/admin/branding "{\"name\":\"Магазин «Ромашка»\",\"logo\":\"$PNG\",\"color\":\"#1F6FEB\",\"theme\":\"dark\"}" | python3 -c 'import sys; c,r=sys.stdin.read().split(" ",1); import json; r=json.loads(r); print(c, r["name"], r["color"], r["theme"], r["logo"][:22])')" \
  "200 Магазин «Ромашка» #1f6feb dark data:image/png;base64,"
check "плагин отдаёт новое оформление" "$(plugin 'r["name"], r["color"], r["theme"], r["logo"] is not None')" "Магазин «Ромашка» #1f6feb dark True"
check "название — displayName realm (письма и заголовок страниц Keycloak)" \
  "$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM" | python3 -c 'import json,sys; print(json.load(sys.stdin)["displayName"])')" "Магазин «Ромашка»"
curl -s -o /dev/null -D "$WORK/h" "$API/auth/login?return_to=/account/"
curl -s -o "$WORK/page.html" "$(header "$WORK/h" location)"
check "страница входа — с новым названием сразу, без пересборки" "$(kc "$WORK/page.html" 'c["realm"]["displayName"]')" "Магазин «Ромашка»"
check "в журнале — изменение оформления, без картинки" \
  "$(curl -s -b "$ADMIN" "$API/auth/admin/audit?pageSize=1" | python3 -c '
import json,sys; i=json.load(sys.stdin)["result"]["items"][0]; c=json.loads(i["changes"]) if isinstance(i["changes"], str) else i["changes"]
print(i["action"], c["logo"]["new"], "base64" in json.dumps(c))')" "ProjectBrandingChanged True False"

echo "3а. Акцент для тёмной темы — свой или тот же"
check "сохранён (строчными), плагин отдаёт оба" \
  "$(api "$ADMIN" PUT /auth/admin/branding "{\"name\":\"Магазин «Ромашка»\",\"logo\":\"$PNG\",\"color\":\"#1f6feb\",\"colorDark\":\"#7AA7FF\",\"theme\":\"dark\"}" | cut -d' ' -f1) $(plugin 'r["color"], r["colorDark"]')" \
  "200 #1f6feb #7aa7ff"
check "без своего тёмного — в ответе плагина его нет (тот же, что основной)" \
  "$(api "$ADMIN" PUT /auth/admin/branding "{\"name\":\"Магазин «Ромашка»\",\"logo\":\"$PNG\",\"color\":\"#1f6feb\",\"theme\":\"dark\"}" | cut -d' ' -f1) $(plugin '"colorDark" in r')" \
  "200 False"

echo "4. Неверное — не сохраняется"
check "цвет не #rrggbb" "$(api "$ADMIN" PUT /auth/admin/branding '{"name":"X","color":"red","theme":"dark"}')" "400 invalid.format"
check "тёмный без основного" "$(api "$ADMIN" PUT /auth/admin/branding '{"name":"X","colorDark":"#7aa7ff","theme":"dark"}')" "400 branding.color_dark_without_color"
check "тёмный не #rrggbb" "$(api "$ADMIN" PUT /auth/admin/branding '{"name":"X","color":"#1f6feb","colorDark":"blue","theme":"dark"}')" "400 invalid.format"
check "логотип — не картинка" "$(api "$ADMIN" PUT /auth/admin/branding '{"name":"X","logo":"data:text/html;base64,PGI+","theme":"dark"}')" "400 invalid.format"
printf '{"name":"X","logo":"data:image/png;base64,%s","theme":"dark"}' "$(head -c 102401 /dev/zero | base64 -w0)" > "$WORK/big.json"
check "логотип больше 100 КБ" "$(api "$ADMIN" PUT /auth/admin/branding "@$WORK/big.json")" "400 branding.logo_too_large"
check "неизвестная тема" "$(api "$ADMIN" PUT /auth/admin/branding '{"name":"X","theme":"neon"}')" "400 value.is.invalid"
check "оформление не изменилось" "$(plugin 'r["name"]')" "Магазин «Ромашка»"

echo "5. Убрать логотип и цвет"
check "null — убрать" "$(api "$ADMIN" PUT /auth/admin/branding '{"name":"Магазин «Ромашка»","logo":null,"color":null,"theme":"system"}' | cut -d' ' -f1)" 200
check "плагин: без логотипа и цвета, тема по системе" "$(plugin '"logo" in r, "color" in r, "colorDark" in r, r["theme"]')" "False False False system"

summary
