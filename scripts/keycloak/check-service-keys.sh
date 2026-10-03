#!/usr/bin/env bash
# Проверка этапа 6а (docs/keycloak/06a-embedding.md): ключи доступа сервисов проекта — выдать, перевыпустить, отозвать.
# Админ проекта входит через страницу, ключи выдаёт через API админки; сервис получает токен своим ключом у Keycloak.
# Нужно разрешение на регистрацию: scripts/keycloak/registration-token.sh --write-env (после пересоздания стенда — снова).
# Запуск: сервис и Keycloak подняты, затем
#   scripts/keycloak/check-service-keys.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
TOKEN_URL="$KC/realms/sandbox/protocol/openid-connect/token"
CODE=check-orders

# admin_call <jar> <метод> <путь> [тело] — запрос к API админки; печатает «<код> <JSON result или код ошибки>»
admin_call() {
  local args=(-s -o "$WORK/r.json" -w '%{http_code}' -b "$1" -c "$1" -X "$2")
  [ -n "${4:-}" ] && args+=(-H 'Content-Type: application/json' -d "$4")
  printf '%s ' "$(curl "${args[@]}" "$API$3")"
  python3 -c '
import json,sys
d=json.load(open(sys.argv[1])) if open(sys.argv[1]).read().strip() else {}
print(json.dumps(d["result"], ensure_ascii=False) if d.get("result") is not None else (d.get("error") or {}).get("messages", [{}])[0].get("code", ""))' "$WORK/r.json"
}
field() { python3 -c 'import json,sys; v=json.loads(sys.argv[1].split(" ",1)[1]); print(eval(sys.argv[2]))' "$1" "$2"; }

# service_token <client_id> <секрет> — «да», если Keycloak выдал токен по ключу, иначе ошибка Keycloak
service_token() {
  curl -s -d grant_type=client_credentials -d "client_id=$1" --data-urlencode "client_secret=$2" "$TOKEN_URL" |
    python3 -c 'import json,sys; d=json.load(sys.stdin); print("да" if "access_token" in d else d.get("error"))'
}
# claims <client_id> <секрет> <python-выражение над c> — поле токена сервиса
claims() {
  curl -s -d grant_type=client_credentials -d "client_id=$1" --data-urlencode "client_secret=$2" "$TOKEN_URL" | python3 -c '
import json,sys,base64
p=json.load(sys.stdin)["access_token"].split(".")[1]; p+="="*(-len(p)%4); c=json.loads(base64.urlsafe_b64decode(p)); print(eval(sys.argv[1]))' "$3"
}
access_token() {
  curl -s -d grant_type=client_credentials -d "client_id=$1" --data-urlencode "client_secret=$2" "$TOKEN_URL" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])'
}
# svc <токен> <путь> — запрос сервиса к нашему API; печатает «<код> <result или код ошибки>»
svc() {
  printf '%s ' "$(curl -s -o "$WORK/s.json" -w '%{http_code}' -H "Authorization: Bearer $1" "$API$2")"
  python3 -c '
import json,sys
t=open(sys.argv[1]).read().strip(); d=json.loads(t) if t else {}
print(json.dumps(d["result"], ensure_ascii=False) if d.get("result") is not None else (d.get("error") or {}).get("messages", [{}])[0].get("code", ""))' "$WORK/s.json"
}
service_sub() {
  curl -s -d grant_type=client_credentials -d "client_id=$1" --data-urlencode "client_secret=$2" "$TOKEN_URL" | python3 -c '
import json,sys,base64
p=json.load(sys.stdin)["access_token"].split(".")[1]; p+="="*(-len(p)%4); print(json.loads(base64.urlsafe_b64decode(p))["sub"])'
}

require_service

echo "1. Вход админа проекта"
ADMIN="$WORK/admin"; : > "$ADMIN"
check "админ проекта вошёл" "$(login "$ADMIN" /me/ 1234567890)" /me/
admin_call "$ADMIN" DELETE "/auth/admin/services/$CODE" > /dev/null # остаток прошлого прогона
check "список сервисов доступен" "$(admin_call "$ADMIN" GET /auth/admin/services | cut -d' ' -f1)" 200

echo "2. Выдача ключа"
R=$(admin_call "$ADMIN" POST /auth/admin/services "{\"code\":\"$CODE\",\"name\":\"Заказы (проверка)\",\"permissions\":[\"service.users.read\",\"service.access.check\"]}")
check "ключ выдан" "$(echo "$R" | cut -d' ' -f1)" 200
CLIENT=$(field "$R" 'v["service"]["clientId"]'); SECRET=$(field "$R" 'v["clientSecret"]')
check "client_id — service-$CODE" "$CLIENT" "service-$CODE"
check "по ключу Keycloak выдаёт токен" "$(service_token "$CLIENT" "$SECRET")" да
SUB=$(service_sub "$CLIENT" "$SECRET")
check "в токене сервиса — права, без ролей пользователя, client_id" \
  "$(claims "$CLIENT" "$SECRET" '",".join(c["permissions"]) + " " + str(c["role"]) + " " + c["client_id"]')" \
  "service.access.check,service.users.read [] service-$CODE"
check "в списке — без секрета" "$(admin_call "$ADMIN" GET /auth/admin/services | python3 -c 'import json,sys; s=[x for x in json.loads(sys.stdin.read().split(" ",1)[1]) if x["code"]=="'$CODE'"]; print(len(s), "clientSecret" in s[0])')" "1 False"
check "повторная выдача того же кода — конфликт" "$(admin_call "$ADMIN" POST /auth/admin/services "{\"code\":\"$CODE\",\"name\":\"x\"}")" "409 services.exists"
check "неверный код — 400" "$(admin_call "$ADMIN" POST /auth/admin/services '{"code":"Заказы","name":"x"}' | cut -d' ' -f1)" 400
check "токен управления в базе — зашифрован" \
  "$(docker compose exec -T postgres sh -c "psql -U \"\$POSTGRES_USER\" -d \"\$POSTGRES_DB\" -tAc \"SELECT left(protected_registration_token, 3) FROM auth.service_keys WHERE code = '$CODE'\"" | tr -d '\r')" \
  CfD

echo "3. Что сервис может у нас по ключу"
ST=$(access_token "$CLIENT" "$SECRET")
CUSTOMER_ID=$(svc "$ST" "/auth/services/users?login=79990000001" | python3 -c 'import json,sys; print(json.loads(sys.stdin.read().split(" ",1)[1])["id"])')
check "найти пользователя по логину" "$(svc "$ST" "/auth/services/users?login=79990000001" | python3 -c 'import json,sys; c,r=sys.stdin.read().split(" ",1); print(c, json.loads(r)["phone"])')" "200 +79990000001"
check "прочитать пользователя по Id" "$(svc "$ST" "/auth/services/users/$CUSTOMER_ID" | python3 -c 'import json,sys; c,r=sys.stdin.read().split(" ",1); print(c, json.loads(r)["email"])')" "200 customer@sandbox.test"
check "право есть: покупатель — profile.view" "$(svc "$ST" "/auth/services/users/$CUSTOMER_ID/access?permission=profile.view" | python3 -c 'import json,sys; c,r=sys.stdin.read().split(" ",1); r=json.loads(r); print(c, r["allowed"], r["activeMode"])')" "200 True customer"
check "права нет: покупатель — users.block" "$(svc "$ST" "/auth/services/users/$CUSTOMER_ID/access?permission=users.block" | python3 -c 'import json,sys; c,r=sys.stdin.read().split(" ",1); r=json.loads(r); print(c, r["allowed"], r["reason"])')" "200 False permission.missing"
BLOCKED_ID=$(svc "$ST" "/auth/services/users?login=79990000004" | python3 -c 'import json,sys; print(json.loads(sys.stdin.read().split(" ",1)[1])["id"])')
check "заблокированный — нет доступа" "$(svc "$ST" "/auth/services/users/$BLOCKED_ID/access?permission=profile.view" | python3 -c 'import json,sys; c,r=sys.stdin.read().split(" ",1); r=json.loads(r); print(r["allowed"], r["reason"])')" "False user.blocked"
check "сервис туда, где нужен человек (/me/) — 403" "$(svc "$ST" /me/ | cut -d' ' -f1)" 403
USER_TOKEN=$(curl -s -d grant_type=password -d client_id=dev-cli -d username=71234567890 -d "password=$PASSWORD" "$TOKEN_URL" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')
check "человек (даже админ) в API сервисов — 403" "$(svc "$USER_TOKEN" "/auth/services/users/$CUSTOMER_ID" | cut -d' ' -f1)" 403
check "без токена — 401" "$(curl -s -o /dev/null -w '%{http_code}' "$API/auth/services/users/$CUSTOMER_ID")" 401
R=$(admin_call "$ADMIN" PUT "/auth/admin/services/$CODE/permissions" '{"permissions":["service.access.check"]}')
check "права сужены: только проверка доступа" "$(echo "$R" | python3 -c 'import json,sys; c,r=sys.stdin.read().split(" ",1); print(c, ",".join(json.loads(r)["permissions"]))')" "200 service.access.check"
check "старый токен (с прежними правами) отозван — 401" "$(svc "$ST" "/auth/services/users/$CUSTOMER_ID" | cut -d' ' -f1)" 401
sleep 1 # отметка отзыва — по секундам: новый токен должен быть выдан позже неё
ST=$(access_token "$CLIENT" "$SECRET")
check "новый токен: читать нельзя — 403" "$(svc "$ST" "/auth/services/users/$CUSTOMER_ID" | cut -d' ' -f1)" 403
check "новый токен: проверять доступ можно — 200" "$(svc "$ST" "/auth/services/users/$CUSTOMER_ID/access?permission=profile.view" | cut -d' ' -f1)" 200

echo "4. Перевыпуск"
R=$(admin_call "$ADMIN" POST "/auth/admin/services/$CODE/rotate")
check "ключ перевыпущен" "$(echo "$R" | cut -d' ' -f1)" 200
NEW_SECRET=$(field "$R" 'v["clientSecret"]')
check "старый секрет Keycloak не принимает" "$(service_token "$CLIENT" "$SECRET")" unauthorized_client
check "новый секрет работает" "$(service_token "$CLIENT" "$NEW_SECRET")" да
check "выданные раньше токены сервиса отозваны (отметка в Redis)" \
  "$(docker compose exec -T redis redis-cli EXISTS "auth:revoked:app:$CLIENT:$SUB" | tr -d '\r')" 1
check "перевыпуск ещё раз (новый токен управления)" "$(admin_call "$ADMIN" POST "/auth/admin/services/$CODE/rotate" | cut -d' ' -f1)" 200

echo "5. Права"
MANAGER="$WORK/manager"; : > "$MANAGER"
login "$MANAGER" /me/ 0987654321 > /dev/null
check "менеджер — 403" "$(admin_call "$MANAGER" GET /auth/admin/services | cut -d' ' -f1)" 403
check "без входа — 401" "$(curl -s -o /dev/null -w '%{http_code}' "$API/auth/admin/services")" 401
# После перевыпуска отозваны и токены той же секунды (отметка включающая) — новый токен берём через секунду.
ROTATED=$(field "$(admin_call "$ADMIN" POST "/auth/admin/services/$CODE/rotate")" 'v["clientSecret"]')
sleep 1
check "сервис своим токеном в админку — 403" \
  "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $(access_token "$CLIENT" "$ROTATED")" "$API/auth/admin/services")" 403

echo "6. Отзыв"
LAST=$(field "$(admin_call "$ADMIN" POST "/auth/admin/services/$CODE/rotate")" 'v["clientSecret"]')
check "ключ отозван" "$(admin_call "$ADMIN" DELETE "/auth/admin/services/$CODE" | cut -d' ' -f1)" 200
check "по отозванному ключу токена нет" "$(service_token "$CLIENT" "$LAST")" invalid_client
check "в списке его нет" "$(admin_call "$ADMIN" GET /auth/admin/services | python3 -c 'import json,sys; print(sum(1 for x in json.loads(sys.stdin.read().split(" ",1)[1]) if x["code"]=="'$CODE'"))')" 0
check "в журнале: выдан, права, перевыпущен, отозван" \
  "$(for a in ServiceKeyIssued ServiceKeyPermissionsChanged ServiceKeyRotated ServiceKeyRevoked; do admin_call "$ADMIN" GET "/auth/admin/audit?action=$a&pageSize=5" | python3 -c 'import json,sys; print(1 if any(i["objectName"]=="service-'$CODE'" for i in json.loads(sys.stdin.read().split(" ",1)[1])["items"]) else 0)'; done | tr -d '\n')" 1111

summary
