#!/usr/bin/env bash
# Проверка этапа 4б (docs/keycloak/04b-admin-ui.md): своя админка на /admin — раздача, безопасность и путь её
# пользователей по API (те же запросы, что делает интерфейс). Сам интерфейс проверяют тесты frontend (Vitest).
# Запуск: сервис (образ с админкой) и Keycloak подняты, затем
#   scripts/keycloak/check-admin-ui.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

# json <jar> <путь> <python-выражение над result> — GET; печатает «<код> <значение или код ошибки>»
json() {
  curl -s -o "$WORK/r.json" -w '%{http_code}' -b "$1" "$API$2"
  printf ' '
  python3 -c '
import json,sys
try: d=json.load(open(sys.argv[1]))
except Exception: print("—"); sys.exit()
r=d.get("result")
print(eval(sys.argv[2]) if d.get("error") is None else d["error"]["messages"][0]["code"])' "$WORK/r.json" "$3"
}

require_service

echo "1. Раздача: /admin из образа сервиса"
curl -s -o "$WORK/index.html" -D "$WORK/h" "$API/admin/"
check "страница админки" "$(head -1 "$WORK/h" | cut -d' ' -f2) $(grep -o '<title>[^<]*' "$WORK/index.html" | cut -c8-)" "200 Админка доступа"
SCRIPT=$(grep -o '/admin/assets/[^"]*\.js' "$WORK/index.html" | head -1)
STYLE=$(grep -o '/admin/assets/[^"]*\.css' "$WORK/index.html" | head -1)
check "скрипт сборки" "$(curl -s -o /dev/null -w '%{http_code} %{content_type}' "$API$SCRIPT")" "200 text/javascript"
check "стили сборки" "$(curl -s -o /dev/null -w '%{http_code} %{content_type}' "$API$STYLE")" "200 text/css"
check "адрес раздела — та же страница (SPA)" \
  "$(curl -s -o "$WORK/deep.html" -w '%{http_code}' "$API/admin/apps/manager-1c/roles") $(cmp -s "$WORK/index.html" "$WORK/deep.html" && echo same)" "200 same"
check "нет такого файла — 404, не страница" "$(status "$WORK/none" GET /admin/assets/nope.js)" 404

echo "2. Безопасность страниц админки"
CSP=$(header "$WORK/h" content-security-policy)
check "CSP: скрипты только свои" "$(starts "$CSP" "default-src 'self'; script-src 'self';")" "default-src 'self'; script-src 'self';"
check "CSP: стили — свои и тег уведомлений по хэшу, без unsafe-inline" \
  "$(grep -c "unsafe-inline" <<< "$CSP") $(grep -o "style-src 'self' 'sha256-[^']*'" <<< "$CSP" | cut -c1-25)" "0 style-src 'self' 'sha256-"
check "CSP: в рамку не встроить" "$(grep -o "frame-ancestors 'none'" <<< "$CSP")" "frame-ancestors 'none'"
check "nosniff" "$(header "$WORK/h" x-content-type-options)" nosniff
check "демо-страница — без CSP админки" "$(curl -s -D - -o /dev/null "$API/" | grep -ci 'content-security-policy')" 0
check "без входа API админки — 401" "$(status "$WORK/none" GET /auth/admin/me)" 401

echo "3. Админ проекта: что показывает админка"
ADMIN="$WORK/admin"; : > "$ADMIN"
login "$ADMIN" /admin/ 1234567890 > /dev/null
check "после входа — обратно в админку" "$(: > "$WORK/again"; login "$WORK/again" /admin/ 1234567890)" /admin/
check "«кто я»: права проекта и все приложения" \
  "$(json "$ADMIN" /auth/admin/me '",".join(a["clientId"] for a in r["apps"]) + "|" + str("audit.view" in r["permissions"])')" \
  "200 manager-1c,sandbox-api|True"
check "названия приложений — без ключей Keycloak \${…}" \
  "$(json "$ADMIN" "/auth/admin/users/$(json "$ADMIN" '/auth/admin/users?search=79990000001' 'r["items"][0]["id"]' | cut -d' ' -f2)/apps" 'str(any(a["name"] and a["name"].startswith("${") for a in r))')" \
  "200 False"
check "журнал по приложению" "$(json "$ADMIN" '/auth/admin/audit?app=manager-1c&pageSize=5' 'str(all(i["app"] == "manager-1c" for i in r["items"]))')" "200 True"
check "роли пользователя: что можно выдать" \
  "$(json "$ADMIN" "/auth/admin/apps/manager-1c/users/$(json "$ADMIN" '/auth/admin/users?search=79990000008' 'r["items"][0]["id"]' | cut -d' ' -f2)/roles" '",".join(x["name"] for x in r["assignableRoles"])')" \
  "200 admin,accountant,operator"

echo "4. Админ «Менеджера 1С»: только своё приложение"
APP_ADMIN="$WORK/app-admin"; : > "$APP_ADMIN"
login "$APP_ADMIN" /admin/ 79990000007 > /dev/null
check "«кто я»: одно приложение, как админ приложения" \
  "$(json "$APP_ADMIN" /auth/admin/me '",".join(a["clientId"] + ":" + str(a["viaProject"]) for a in r["apps"]) + "|" + ",".join(r["permissions"])')" \
  "200 manager-1c:False|profile.view"
check "вкладки: пользователи, роли и права, журнал" \
  "$(json "$APP_ADMIN" /auth/admin/me '",".join(sorted(r["apps"][0]["permissions"]))')" "200 audit.view,roles.manage,users.block,users.roles.manage"
check "пользователи 1С" "$(json "$APP_ADMIN" /auth/admin/apps/manager-1c/users '",".join(u["username"] for u in r["items"])')" "200 79990000007,79990000008"
check "роли и права 1С" "$(json "$APP_ADMIN" /auth/admin/apps/manager-1c/roles 'str(len(r))')" "200 3"
check "журнал 1С" "$(json "$APP_ADMIN" '/auth/admin/apps/manager-1c/audit?pageSize=5' 'str(all(i["app"] == "manager-1c" for i in r["items"]))')" "200 True"
check "раздел «Пользователи» проекта — 403" "$(status "$APP_ADMIN" GET /auth/admin/users)" 403
check "журнал проекта — 403" "$(status "$APP_ADMIN" GET /auth/admin/audit)" 403
check "роли приложения сервиса — 403" "$(json "$APP_ADMIN" /auth/admin/apps/sandbox-api/roles 'r')" "403 apps.forbidden"

echo "5. Покупатель: «нет доступа»"
CUSTOMER="$WORK/customer"; : > "$CUSTOMER"
login "$CUSTOMER" /admin/ 79990000001 > /dev/null
check "«кто я»: приложений нет" "$(json "$CUSTOMER" /auth/admin/me 'str(len(r["apps"])) + "|" + ",".join(r["permissions"])')" "200 0|profile.view"

for JAR in "$ADMIN" "$APP_ADMIN" "$CUSTOMER"; do status "$JAR" POST /auth/logout > /dev/null; done
summary
