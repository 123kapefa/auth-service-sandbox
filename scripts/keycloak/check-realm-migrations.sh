#!/usr/bin/env bash
# Проверка этапа 11.6 (docs/migrations.md): мигратор настроек проектов (realm). Realm песочницы откатывается к виду
# «создан до этапа 6б» (без кабинета, оформления, смены номера, предложения ключа, настроек входа и капчи, без версии),
# затем — развёртывание: контейнер realm-migrator, как при каждом docker compose up. Он применяет все миграции, realm —
# как из шаблона; повторный запуск ничего не меняет. После проверки realm снова в порядке — дальше check-6b.sh и
# check-10.sh проверяют кабинет и вход на этом, мигрированном, realm.
# Запуск: стенд поднят (docker compose up -d --build --wait), затем
#   scripts/keycloak/check-realm-migrations.sh
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM=sandbox
CAPTCHA=https://smartcaptcha.yandexcloud.net
LATEST=$(ls "$ROOT"/scripts/project/realm-migrations/[0-9][0-9][0-9][0-9]-*.py | wc -l | tr -d ' ')

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

MASTER=$(master_token)
# kc_get <путь Admin API> <python-выражение над r>
kc_get() { curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM$1" | python3 -c "import json,sys; r=json.load(sys.stdin); print($2)"; }
kc_put() { curl -s -o /dev/null -w '%{http_code}' -X "$1" -H "Authorization: Bearer $MASTER" -H 'Content-Type: application/json' -d "$3" "$KC/admin/realms/$REALM$2"; }
BFF_ID=$(kc_get "/clients?clientId=sandbox-bff" 'r[0]["id"]')
API_ID=$(kc_get "/clients?clientId=sandbox-api" 'r[0]["id"]')
SA_ID=$(kc_get "/clients/$BFF_ID/service-account-user" 'r["id"]')

# Всё, что дают миграции 0001 (6б) и 0002 (10), — одной строкой.
migrated() {
  echo "почта: $(kc_get /authentication/required-actions/UPDATE_EMAIL 'r["enabled"], r["config"].get("verifyEmail")')" \
    "· возврат: $(kc_get "/clients/$BFF_ID" 'bool(r.get("baseUrl"))')" \
    "· учётка: $(kc_get "/users/$SA_ID/role-mappings/realm" 'sorted(x["name"] for x in r if x["name"].startswith("manage-"))')" \
    "· admin: $(kc_get "/clients/$API_ID/roles/admin/composites" 'sorted(x["name"] for x in r if x["name"] in ("branding.manage", "sign-in.manage"))')" \
    "· номер: $(kc_get /authentication/required-actions/platform-update-phone 'r["enabled"]')" \
    "· ключ: $(kc_get /authentication/required-actions/platform-passkey-offer 'r["enabled"]')" \
    "· капча: $(kc_get "" '"'"$CAPTCHA"'" in r["browserSecurityHeaders"]["contentSecurityPolicy"]')"
}
# Снимок realm, который могла тронуть миграция: настройки, действия, роли, клиент входа, состав admin.
snapshot() {
  for path in "" /authentication/required-actions /roles "/clients/$BFF_ID" "/clients/$API_ID/roles/admin/composites" \
      "/users/$SA_ID/role-mappings/realm"; do
    curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM$path"
  done | sha256sum | cut -d' ' -f1
}
# deploy — развёртывание: контейнер мигратора, как при docker compose up. Печатает его вывод.
deploy() { docker compose --project-directory "$ROOT" --progress quiet run --rm --no-deps realm-migrator 2>&1; }

TEMPLATE="почта: True true · возврат: True · учётка: ['manage-branding', 'manage-sign-in'] · admin: ['branding.manage', 'sign-in.manage'] · номер: True · ключ: True · капча: True"

echo "1. Стенд из шаблона"
check "realm — как из шаблона, версия — последняя" "$(migrated) / $(kc_get "" 'r["attributes"].get("platform.realmVersion")')" "$TEMPLATE / $LATEST"
check "мигратору делать нечего" "$(deploy | head -1)" "$REALM: версия $LATEST, изменений нет"

echo "2. Realm, созданный до этапа 6б"
kc_put PUT /authentication/required-actions/UPDATE_EMAIL \
  "$(kc_get /authentication/required-actions/UPDATE_EMAIL 'json.dumps({**r, "enabled": False, "config": {}})')" > /dev/null
for action in platform-update-phone platform-passkey-offer; do
  kc_put PUT "/authentication/required-actions/$action" \
    "$(kc_get "/authentication/required-actions/$action" 'json.dumps({**r, "enabled": False})')" > /dev/null
done
kc_put PUT "/clients/$BFF_ID" "$(kc_get "/clients/$BFF_ID" 'json.dumps({**r, "baseUrl": ""})')" > /dev/null
kc_put DELETE "/clients/$API_ID/roles/admin/composites" \
  "[$(kc_get "/clients/$API_ID/roles/branding.manage" 'json.dumps(r)'), $(kc_get "/clients/$API_ID/roles/sign-in.manage" 'json.dumps(r)')]" > /dev/null
kc_put DELETE /roles/manage-branding "" > /dev/null
kc_put DELETE /roles/manage-sign-in "" > /dev/null
CSP_OLD=$(kc_get "" 'r["browserSecurityHeaders"]["contentSecurityPolicy"].replace(" '"$CAPTCHA"'", "")')
kc_put PUT "" "$(kc_get "" 'json.dumps({"browserSecurityHeaders": {**r["browserSecurityHeaders"], "contentSecurityPolicy": "'"$CSP_OLD"'"}, "attributes": {"platform.realmVersion": None}})')" > /dev/null
check "до развёртывания: ничего из 6б и 10" "$(migrated)" \
  "почта: False None · возврат: False · учётка: [] · admin: [] · номер: False · ключ: False · капча: False"
check "до развёртывания: версии нет" "$(kc_get "" 'r["attributes"].get("platform.realmVersion")') / $("$ROOT/scripts/project/migrate-realms.py" --status "$REALM")" \
  "None / $REALM: версия 0 из $LATEST"

echo "3. Развёртывание: мигратор"
OUT=$(deploy)
check "мигратор применил все миграции по порядку" \
  "$(echo "$OUT" | head -1) / $(echo "$OUT" | sed -n 's/^  \([0-9]*-[a-z0-9-]*\):.*/\1/p' | tr '\n' ' ')" \
  "$REALM: версия 0 → $LATEST / $(cd "$ROOT/scripts/project/realm-migrations" && ls [0-9]*.py | sed 's/\.py$//' | tr '\n' ' ')"
check "realm — как из шаблона, версия — последняя" "$(migrated) / $(kc_get "" 'r["attributes"].get("platform.realmVersion")')" "$TEMPLATE / $LATEST"
check "CSP — как была, с капчей" "$(kc_get "" 'r["browserSecurityHeaders"]["contentSecurityPolicy"]')" \
  "$(echo "$CSP_OLD" | sed "s#frame-src 'self'#frame-src 'self' $CAPTCHA#")"

echo "4. Повторное развёртывание"
BEFORE=$(snapshot)
check "мигратор: изменений нет" "$(deploy | head -1)" "$REALM: версия $LATEST, изменений нет"
check "realm не изменился (снимок настроек, действий, ролей, клиентов)" "$(snapshot)" "$BEFORE"

echo "5. Версия новее мигратора (развернули старую версию сервиса)"
kc_put PUT "" "{\"attributes\":{\"platform.realmVersion\":\"$((LATEST + 1))\"}}" > /dev/null
check "мигратор пропускает realm, не падает" "$(deploy | head -1; echo "код ${PIPESTATUS[0]}")" \
  "$REALM: версия $((LATEST + 1)) новее мигратора ($LATEST) — пропущен (развёрнута старая версия?)
код 0"
kc_put PUT "" "{\"attributes\":{\"platform.realmVersion\":\"$LATEST\"}}" > /dev/null

summary
