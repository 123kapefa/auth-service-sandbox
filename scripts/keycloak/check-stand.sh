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

T=$(token 1234567890)
check "admin в режиме admin: active_mode" "$(echo "$T" | claim active_mode)" admin
T=$(token 0987654321)
check "manager в режиме manager: active_mode" "$(echo "$T" | claim active_mode)" manager

echo "3. Два адреса Keycloak"
# Запрос «как из контейнера» (Host: keycloak:8180): iss — внешний адрес, ключи — внутренний.
D=$(curl -s -H "Host: keycloak:8180" "$KC/realms/$REALM/.well-known/openid-configuration")
check "iss не зависит от адреса запроса" "$(echo "$D" | claim issuer)" "$KC/realms/$REALM"
check "jwks_uri — по адресу запроса" "$(echo "$D" | claim jwks_uri)" "http://keycloak:8180/realms/$REALM/protocol/openid-connect/certs"

echo "4. Защита"
check "заблокированный не получает токен" "$(token 79990000004 | claim error_description)" "Account disabled"
check "неверный пароль отклонён" "$(curl -s -d grant_type=password -d client_id=dev-cli -d username=79990000001 -d password=wrong \
  "$KC/realms/$REALM/protocol/openid-connect/token" | claim error)" invalid_grant

echo "5. Способы входа"
check "вход по почте — тот же пользователь" "$(token admin@sandbox.test | claim phone)" +71234567890
check "вход по телефону по-прежнему работает" "$(token 1234567890 | claim phone)" +71234567890
check "поле входа подписано «Телефон или почта»" \
  "$(curl -s "$KC/realms/$REALM/protocol/openid-connect/auth?client_id=sandbox-bff&response_type=code&scope=openid&redirect_uri=http://localhost:8080/auth/callback&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256" | grep -c 'Телефон или почта' | tr -d ' ' | sed 's/^[1-9][0-9]*$/да/')" да

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

echo
echo "Итого: $PASS ✓, $FAIL ✗"
[ "$FAIL" -eq 0 ]
