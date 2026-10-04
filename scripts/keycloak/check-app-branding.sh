#!/usr/bin/env bash
# Проверка шага 6б.5 (docs/keycloak/06b-account-branding.md): оформление приложения поверх проекта (название, логотип,
# цвет — атрибуты клиента приложения), на страницах входа в приложение, в письмах (серый стиль, логотип ссылкой) и в
# личном кабинете, открытом из приложения. В конце оформление приложения и проекта возвращается.
# Запуск: стенд поднят (Docker: сервис :8080, Keycloak :8180, Mailpit :8025), затем
#   scripts/keycloak/check-app-branding.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
MAILPIT="${MAILPIT:-http://localhost:${MAILPIT_UI_PORT:-8025}}"
REALM=sandbox
APP=manager-1c
PLUGIN="$KC/realms/$REALM/platform-branding"
RESET_LOGIN=79990000005
RESET_EMAIL=reset@sandbox.test
# Картинка 1×1 PNG.
PNG='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='

# api <jar> <метод> <путь> [тело] — печатает «<код> <код ошибки или result>»
api() {
  curl -s -o "$WORK/r.json" -w '%{http_code}' -b "$1" -X "$2" -H 'Content-Type: application/json' ${4:+-d "$4"} "$API$3"
  printf ' '
  python3 -c '
import json,sys
try: d=json.load(open(sys.argv[1]))
except Exception: print("—"); sys.exit()
print(d["error"]["messages"][0]["code"] if d.get("error") else json.dumps(d["result"], ensure_ascii=False, sort_keys=True))' "$WORK/r.json"
}
# json <python-выражение над r> — разбор JSON со stdin
json() { python3 -c "import json,sys; r=json.load(sys.stdin); print($1)"; }
# mail <адрес> <python-выражение над html, text, subject> — последнее письмо на адрес (ждём до 10 секунд)
mail() {
  local out
  for _ in 1 2 3 4 5 6 7 8 9 10; do
    out=$(curl -s "$MAILPIT/api/v1/search?query=to:$1" | python3 -c '
import json,sys,urllib.request
messages = json.load(sys.stdin)["messages"]
if messages:
    m = json.load(urllib.request.urlopen(sys.argv[1] + "/api/v1/message/" + messages[0]["ID"]))
    html, text, subject = m["HTML"] or "", m["Text"] or "", m["Subject"]
    v = eval(sys.argv[2])
    print(*v) if isinstance(v, tuple) else print(v)' "$MAILPIT" "$2")
    [ -n "$out" ] && { echo "$out"; return; }
    sleep 1
  done
}
# login_page <jar> <app или пусто> — страница входа через сервис; печатает оформление, которое у неё прочитает тема
login_page() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html" query
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=/account/${2:+&app=$2}"
  curl -s -o "$page" -b "$jar" -c "$jar" "$(header "$h" location)"
  # Как branding.ts: client_id и tab_id из kcContext, cookie сессии входа — у браузера.
  query=$(kc "$page" '"client_id=" + c["client"]["clientId"] + "&tab_id=" + __import__("re").search(r"tab_id=([^&]+)", c["url"]["loginAction"]).group(1)')
  curl -s -b "$jar" "$PLUGIN?$query" | json 'r["name"], r.get("color")'
}

require_service
ADMIN="$WORK/admin"; : > "$ADMIN"
MANAGER="$WORK/manager"; : > "$MANAGER"
CUSTOMER="$WORK/customer"; : > "$CUSTOMER"
login "$ADMIN" /admin/ 1234567890 > /dev/null
login "$MANAGER" /admin/ 0987654321 > /dev/null
login "$CUSTOMER" /account/ 79990000001 > /dev/null
PROJECT=$(curl -s -b "$ADMIN" "$API/auth/admin/branding" | json 'json.dumps(r["result"])')
OWN=$(curl -s -b "$ADMIN" "$API/auth/admin/apps/$APP/branding" | json 'json.dumps({k: r["result"][k] for k in ("name", "logo", "color", "colorDark")})')
trap 'curl -s -o /dev/null -b "$ADMIN" -X PUT -H "Content-Type: application/json" -d "$PROJECT" "$API/auth/admin/branding"
      curl -s -o /dev/null -b "$ADMIN" -X PUT -H "Content-Type: application/json" -d "$OWN" "$API/auth/admin/apps/$APP/branding"
      rm -rf "$WORK"' EXIT
api "$ADMIN" PUT /auth/admin/branding '{"name":"Магазин","logo":null,"color":"#1f6feb","colorDark":"#7aa7ff","theme":"system"}' > /dev/null
api "$ADMIN" PUT "/auth/admin/apps/$APP/branding" '{"name":null,"logo":null,"color":null}' > /dev/null

echo "1. Плагин Keycloak: своё у приложения читать — всем, менять — только учётке с manage-branding"
check "без своего — всё как у проекта" "$(curl -s "$PLUGIN/apps/$APP" | json 'sorted(r)')" "[]"
check "без токена не поменять" \
  "$(curl -s -o /dev/null -w '%{http_code}' -X PUT -H 'Content-Type: application/json' -d '{"name":"x"}' "$PLUGIN/apps/$APP")" 401
check "нет такого клиента — 404 app_not_found (не «нет плагина»)" \
  "$(curl -s -w ' %{http_code}' "$PLUGIN/apps/nope" | sed 's/{"error":"\(.*\)"}/\1/')" "app_not_found 404"

echo "2. Админка: оформление приложения — только админ проекта"
check "в «кто я» у админа в приложении — branding.manage (вкладка «Оформление»)" \
  "$(curl -s -b "$ADMIN" "$API/auth/admin/me" | json '"branding.manage" in next(a for a in r["result"]["apps"] if a["clientId"] == "'$APP'")["permissions"]')" True
check "менеджеру — нет" "$(api "$MANAGER" GET "/auth/admin/apps/$APP/branding" | cut -d' ' -f1)" 403
check "клиент только для входа — не приложение" "$(api "$ADMIN" PUT /auth/admin/apps/sandbox-bff/branding '{"name":"x"}')" "404 apps.not_found"
check "цвет не #rrggbb — нет" "$(api "$ADMIN" PUT "/auth/admin/apps/$APP/branding" '{"color":"#fff"}')" "400 invalid.format"

echo "3. Своё у приложения: название, логотип, цвет"
check "сохранено (цвет — строчными), проект — рядом" \
  "$(api "$ADMIN" PUT "/auth/admin/apps/$APP/branding" "{\"name\":\"Менеджер 1С\",\"logo\":\"$PNG\",\"color\":\"#AA3300\"}" \
    | python3 -c 'import sys,json; c,r=sys.stdin.read().split(" ",1); r=json.loads(r); print(c, r["name"], r["color"], r["logo"][:10], r["project"]["name"])')" \
  "200 Менеджер 1С #aa3300 data:image Магазин"
check "у клиента — атрибуты, у проекта — без изменений" \
  "$(curl -s "$PLUGIN?app=$APP" | json 'r["name"], r["color"], r["theme"]') / $(curl -s "$PLUGIN" | json 'r["name"], r["color"]')" \
  "Менеджер 1С #aa3300 system / Магазин #1f6feb"
check "свой акцент — парой: тёмный проекта не берётся (свой цвет и в тёмной теме)" \
  "$(curl -s "$PLUGIN?app=$APP" | json '"colorDark" in r') / $(curl -s "$PLUGIN" | json 'r["colorDark"]')" "False / #7aa7ff"
check "тёмный без своего основного — нет" "$(api "$ADMIN" PUT "/auth/admin/apps/$APP/branding" '{"colorDark":"#ff8866"}')" "400 branding.color_dark_without_color"
check "в журнале приложения — изменение, без картинки" \
  "$(curl -s -b "$ADMIN" "$API/auth/admin/audit?pageSize=1" | json '(lambda i: (i["action"], i["app"], "base64" in json.dumps(i["changes"])))(r["result"]["items"][0])')" \
  "('AppBrandingChanged', '$APP', False)"

echo "4. Страница входа: вход в приложение — его оформление, просто вход — проекта"
check "вход через сервис с app=$APP" "$(login_page "$WORK/b1" "$APP")" "Менеджер 1С #aa3300"
check "вход без app" "$(login_page "$WORK/b2" "")" "Магазин #1f6feb"
check "неизвестный app — оформление проекта" "$(login_page "$WORK/b3" nope)" "Магазин #1f6feb"

echo "5. Письма: серый стиль, название, цвет и логотип ссылкой"
curl -s -X DELETE "$MAILPIT/api/v1/search?query=to:$RESET_EMAIL" > /dev/null
JAR="$WORK/b4"; : > "$JAR"
curl -s -o /dev/null -D "$WORK/h" -b "$JAR" -c "$JAR" "$API/auth/login?return_to=/account/&app=$APP"
curl -s -o "$WORK/page.html" -b "$JAR" -c "$JAR" "$(header "$WORK/h" location)"
curl -s -o "$WORK/page.html" -b "$JAR" -c "$JAR" "$KC$(kc "$WORK/page.html" 'c["url"]["loginResetCredentialsUrl"]')"
curl -s -o "$WORK/page.html" -b "$JAR" -c "$JAR" --data-urlencode "username=$RESET_LOGIN" "$(form_action "$WORK/page.html")"
check "сброс пароля со входа в приложение — от его имени, кнопка его цветом, линии серые" \
  "$(mail "$RESET_EMAIL" '"Менеджер 1С" in html, "Менеджер 1С" in text, "background:#aa3300" in html, "solid #aa3300" in html, "border-radius" in html')" \
  "True True True False False"
LOGO=$(mail "$RESET_EMAIL" '__import__("html").unescape(__import__("re").search(r"<img src=\"([^\"]+)\"", html).group(1))')
check "логотип — ссылкой на плагин приложения" "$(starts "$LOGO" "$KC/realms/$REALM/platform-branding/logo?")" "$KC/realms/$REALM/platform-branding/logo?"
check "по ссылке — картинка, в песочнице (CSP sandbox)" \
  "$(curl -s -o /dev/null -w '%{http_code} %{content_type} %header{content-security-policy}' "$LOGO" | grep -o '^200 image/png.*sandbox$' | cut -d' ' -f1,2)" "200 image/png"
ID_RESET=$(curl -s -H "Authorization: Bearer $(master_token)" "$KC/admin/realms/$REALM/users?username=$RESET_LOGIN&exact=true" | json 'r[0]["id"]')
curl -s -X DELETE "$MAILPIT/api/v1/search?query=to:$RESET_EMAIL" > /dev/null
api "$ADMIN" POST "/auth/admin/users/$ID_RESET/password-reset" > /dev/null
check "ссылка из админки (не из входа) — от имени проекта, его цветом, без логотипа" \
  "$(mail "$RESET_EMAIL" '"Магазин" in html, "Менеджер 1С" in html, "background:#1f6feb" in html, "<img" in html')" \
  "True False True False"

echo "6. Личный кабинет, открытый из приложения"
check "/auth/account?app=… — в кабинет с app" \
  "$(curl -s -o /dev/null -w '%{redirect_url}' "$API/auth/account?app=$APP" | sed "s|^$API||")" "/account/?app=$APP"
check "оформление кабинета из приложения" "$(curl -s -b "$CUSTOMER" "$API/me/branding?app=$APP" | json 'r["result"]["name"], r["result"]["color"]')" "Менеджер 1С #aa3300"
api "$ADMIN" PUT "/auth/admin/apps/$APP/branding" "{\"name\":\"Менеджер 1С\",\"logo\":\"$PNG\",\"color\":\"#aa3300\",\"colorDark\":\"#FF8866\"}" > /dev/null
check "свой тёмный акцент — в кабинет (строчными)" "$(curl -s -b "$CUSTOMER" "$API/me/branding?app=$APP" | json 'r["result"]["color"], r["result"]["colorDark"]')" "#aa3300 #ff8866"
check "без app — проекта" "$(curl -s -b "$CUSTOMER" "$API/me/branding" | json 'r["result"]["name"], r["result"]["color"], r["result"]["colorDark"]')" "Магазин #1f6feb #7aa7ff"
check "без входа — нет" "$(curl -s -o /dev/null -w '%{http_code}' "$API/me/branding")" 401

echo "7. Вернуть «как у проекта»"
check "null — как у проекта" "$(api "$ADMIN" PUT "/auth/admin/apps/$APP/branding" '{"name":null,"logo":null,"color":null,"colorDark":null}' | cut -d' ' -f1)" 200
check "вход в приложение — снова оформление проекта" "$(login_page "$WORK/b5" "$APP")" "Магазин #1f6feb"

summary
