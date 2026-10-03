#!/usr/bin/env bash
# Проверка этапа 1 (docs/keycloak/01-stand.md): Keycloak поднят, realm `sandbox` импортирован,
# токен содержит контракт. Запуск: docker compose up -d --wait keycloak && scripts/keycloak/check-stand.sh
set -uo pipefail

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM=sandbox
PASS=0; FAIL=0
ok()    { PASS=$((PASS+1)); printf '  \033[32m✓\033[0m %s\n' "$1"; }
bad()   { FAIL=$((FAIL+1)); printf '  \033[31m✗\033[0m %s\n' "$1"; }
check() { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1 (ожидали '$3', получили '$2')"; fi; }

# token <username> → JSON claims access-токена, либо JSON ошибки
token() {
  curl -s -d grant_type=password -d client_id=dev-cli -d "username=$1" -d 'password=Test1234!' \
    "$KC/realms/$REALM/protocol/openid-connect/token" | python3 -c '
import json,sys,base64
d=json.load(sys.stdin)
if "access_token" not in d: print(json.dumps(d)); sys.exit()
p=d["access_token"].split(".")[1]; p+="="*(-len(p)%4); print(base64.urlsafe_b64decode(p).decode())'
}
claim() { python3 -c "import json,sys; v=json.load(sys.stdin).get('$1'); print(','.join(sorted(v)) if isinstance(v,list) else v)"; }

echo "1. Keycloak и realm"
check "discovery отвечает" "$(curl -s -o /dev/null -w '%{http_code}' "$KC/realms/$REALM/.well-known/openid-configuration")" 200
check "issuer" "$(curl -s "$KC/realms/$REALM/.well-known/openid-configuration" | claim issuer)" "$KC/realms/$REALM"
check "JWKS содержит RS256-ключ" "$(curl -s "$KC/realms/$REALM/protocol/openid-connect/certs" | python3 -c 'import json,sys; print(any(k["alg"]=="RS256" for k in json.load(sys.stdin)["keys"]))')" True

echo "2. Контракт токена"
T=$(token 79990000001)
check "customer: aud"              "$(echo "$T" | claim aud)" sandbox-api
check "customer: role"             "$(echo "$T" | claim role)" customer
check "customer: active_mode"      "$(echo "$T" | claim active_mode)" customer
check "customer: phone"            "$(echo "$T" | claim phone)" +79990000001
T=$(token 79990000002)
check "admin: role"                "$(echo "$T" | claim role)" admin,customer
T=$(token 79990000003)
check "manager: available_roles" "$(echo "$T" | claim available_roles)" customer,manager
check "нет служебных ролей Keycloak" "$(echo "$T" | claim realm_access)" None
check "нет ролей клиентов Keycloak (resource_access)" "$(echo "$T" | claim resource_access)" None

T=$(token 71234567890)
check "admin в режиме admin: active_mode" "$(echo "$T" | claim active_mode)" admin
T=$(token 70987654321)
check "manager в режиме manager: active_mode" "$(echo "$T" | claim active_mode)" manager

echo "3. Два адреса Keycloak"
# Запрос «как из контейнера» (Host: keycloak:8180): iss — внешний адрес, ключи — внутренний.
D=$(curl -s -H "Host: keycloak:8180" "$KC/realms/$REALM/.well-known/openid-configuration")
check "iss не зависит от адреса запроса" "$(echo "$D" | claim issuer)" "$KC/realms/$REALM"
check "jwks_uri — по адресу запроса" "$(echo "$D" | claim jwks_uri)" "http://keycloak:8180/realms/$REALM/protocol/openid-connect/certs"

echo "4. Защита"
# Мастер-админка (логин и пароль — из .env): нужна здесь и в разделе 5.
# shellcheck source=master-token.sh
source "$(dirname "$0")/master-token.sh"
MASTER=$(master_token)
# Каждая попытка заблокированного — неудача для защиты от перебора: после многих прогонов подряд его временно
# запирает, и Keycloak отвечает «Invalid user credentials» вместо «Account disabled». Счётчик — с нуля.
BLOCKED_ID=$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/users?username=79990000004&exact=true" | python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])')
curl -s -o /dev/null -X DELETE -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/attack-detection/brute-force/users/$BLOCKED_ID"
check "заблокированный не получает токен" "$(token 79990000004 | claim error_description)" "Account disabled"
check "неверный пароль отклонён" "$(curl -s -d grant_type=password -d client_id=dev-cli -d username=79990000001 -d password=wrong \
  "$KC/realms/$REALM/protocol/openid-connect/token" | claim error)" invalid_grant

echo "5. Способы входа"
check "вход по почте — тот же пользователь" "$(token admin@sandbox.test | claim phone)" +71234567890
check "вход по телефону по-прежнему работает" "$(token 71234567890 | claim phone)" +71234567890
AUTH_URL="$KC/realms/$REALM/protocol/openid-connect/auth?client_id=sandbox-bff&response_type=code&scope=openid&redirect_uri=http://localhost:8080/auth/callback&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256"
# Страницу рисует тема Keycloakify (React) в браузере; подписи и кнопки проверяют тесты login-theme, здесь — что
# Keycloak отдаёт нашу тему с нужными настройками (kcContext страницы).
LOGIN_PAGE="$(mktemp)"; curl -s -o "$LOGIN_PAGE" "$AUTH_URL"
page() { python3 "$(dirname "$0")/kc_context.py" "$LOGIN_PAGE" "$1"; }
check "страница входа — тема sandbox (Keycloakify)" "$(page 'c["templateName"] + " " + c["url"]["resourcesUrl"].split("/")[-1]')" "login.ftl sandbox"
SCRIPT=$(grep -o 'src="[^"]*/assets/index-[^"]*\.js"' "$LOGIN_PAGE" | head -1 | cut -d'"' -f2)
check "скрипт темы отдаётся" "$(curl -s -o /dev/null -w '%{http_code} %{content_type}' "$KC$SCRIPT" | cut -d';' -f1)" "200 text/javascript"
check "язык по умолчанию — русский" "$(page 'c["locale"]["currentLanguageTag"]')" ru
check "вход по ключу доступа (passkey) предложен" "$(page 'c["enableWebAuthnConditionalUI"]')" True
check "«Забыли пароль?» — сброс разрешён" "$(page 'str(c["realm"]["resetPasswordAllowed"]) + " " + str("reset-credentials" in c["url"]["loginResetCredentialsUrl"])')" "True True"
rm -f "$LOGIN_PAGE"

echo "6. Вход в приложение (клиент sandbox-bff)"
# authorize <redirect_uri> [pkce] → "<HTTP-код> <куда редирект>"
authorize() {
  local pkce=""
  [ "${2:-}" = pkce ] && pkce="&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256"
  curl -s -o /dev/null -w '%{http_code} %{redirect_url}' \
    "$KC/realms/$REALM/protocol/openid-connect/auth?client_id=sandbox-bff&response_type=code&scope=openid&state=s1&redirect_uri=$1$pkce"
}
check "наш callback → страница входа" "$(authorize http://localhost:8080/auth/callback pkce | cut -d' ' -f1)" 200
check "чужой redirect_uri отклонён" "$(authorize https://evil.example/cb pkce | cut -d' ' -f1)" 400
check "без PKCE — ошибка invalid_request" \
  "$(authorize http://localhost:8080/auth/callback | grep -o 'error=invalid_request' || echo 'нет ошибки')" error=invalid_request

echo "7. Служебная учётка sandbox-bff — Admin API (этап 4)"
SECRET="${SANDBOX_BFF_CLIENT_SECRET:-$(grep -s '^SANDBOX_BFF_CLIENT_SECRET=' "$(dirname "$0")/../../.env" | cut -d= -f2)}"
SA=$(curl -s -d grant_type=client_credentials -d client_id=sandbox-bff --data-urlencode "client_secret=$SECRET" \
  "$KC/realms/$REALM/protocol/openid-connect/token" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("access_token",""))')
check "токен служебной учётки (client_credentials)" "$([ -n "$SA" ] && echo да || echo нет)" да
admin_api() { curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $SA" "$KC/admin/realms/$REALM/$1"; }
check "читает пользователей (view-users)" "$(admin_api 'users?username=79990000001&exact=true')" 200
check "читает события входа (view-events)" "$(admin_api 'events?max=1')" 200
# Точечные права (FGAP v2): из клиентов — только клиенты приложений (выдавать их роли), чужие клиенты и их секреты не видны.
check "из клиентов видит только клиенты приложений" "$(curl -s -H "Authorization: Bearer $SA" "$KC/admin/realms/$REALM/clients" | python3 -c 'import json,sys; print(",".join(sorted(c["clientId"] for c in json.load(sys.stdin))))')" account-console,manager-1c,sandbox-api,sandbox-bff
check "чужой клиент (broker) даже не находит" "$(curl -s -H "Authorization: Bearer $SA" "$KC/admin/realms/$REALM/clients?clientId=broker&search=true")" "[]"
# Настоящий id broker — мастер-админкой, чтобы проверить сам запрет на секрет.
BROKER_ID=$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients?clientId=broker" | python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])')
check "секрет чужого клиента не читает" "$(admin_api "clients/$BROKER_ID/client-secret")" 403
check "не меняет настройки realm" "$(curl -s -o /dev/null -w '%{http_code}' -X PUT -H "Authorization: Bearer $SA" \
  -H 'Content-Type: application/json' -d '{"displayName":"x"}' "$KC/admin/realms/$REALM")" 403
check "токен без секрета не выдаётся" "$(curl -s -o /dev/null -w '%{http_code}' -d grant_type=client_credentials -d client_id=sandbox-bff \
  "$KC/realms/$REALM/protocol/openid-connect/token")" 401

echo "8. Роли и права приложения в Keycloak (этап 4а)"
API_ID=2631bc45-b176-4774-927a-b6dd4e310582   # фиксированный id клиента sandbox-api в realm JSON
composites() { curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients/$API_ID/roles/$1/composites" \
  | python3 -c 'import json,sys; print(",".join(sorted(r["name"] for r in json.load(sys.stdin))))'; }
check "customer = profile.view" "$(composites customer)" profile.view
check "manager = просмотр и блокировка пользователей" "$(composites manager)" profile.view,users.block,users.view
check "admin = все права" "$(composites admin)" audit.view,profile.view,roles.manage,services.manage,users.block,users.credentials.manage,users.roles.manage,users.sessions.manage,users.view
check "права помечены kind=permission" "$(curl -s -H "Authorization: Bearer $MASTER" "$KC/admin/realms/$REALM/clients/$API_ID/roles?briefRepresentation=false" \
  | python3 -c 'import json,sys; r=json.load(sys.stdin); print(",".join(sorted(x["name"] for x in r if x.get("attributes",{}).get("kind")==["permission"])))')" \
  audit.view,profile.view,roles.manage,service.access.check,service.users.read,services.manage,users.block,users.credentials.manage,users.roles.manage,users.sessions.manage,users.view
check "в role токена — только роли, без прав" "$(token 71234567890 | claim role)" admin,customer
check "permissions — права активного режима: админ в режиме admin" "$(token 71234567890 | claim permissions)" \
  audit.view,profile.view,roles.manage,services.manage,users.block,users.credentials.manage,users.roles.manage,users.sessions.manage,users.view
check "админ в режиме customer — только права покупателя" "$(token 79990000002 | claim permissions)" profile.view
check "менеджер в режиме manager" "$(token 70987654321 | claim permissions)" profile.view,users.block,users.view

echo
echo "Итого: $PASS ✓, $FAIL ✗"
[ "$FAIL" -eq 0 ]
