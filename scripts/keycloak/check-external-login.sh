#!/usr/bin/env bash
# Проверка входа через Госуслуги и Яндекс ID (docs/keycloak/09-external-login.md) — на заглушке idp-stub: весь путь
# браузера (страница входа → провайдер → Keycloak → сервис), новая учётная запись, привязка к существующей по
# телефону, отказ у провайдера, подписи запросов к ЕСИА, закрытый доступ к приложению после внешнего входа.
# Запуск: стенд поднят (docker compose up -d --build --wait), затем
#   scripts/keycloak/check-external-login.sh
# Повторный запуск — с начала: учётные записи прошлых прогонов удаляются, привязка покупателя — снимается.
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
STUB="${STUB:-http://localhost:${IDP_STUB_PORT:-8190}}"
REALM=sandbox
# Секрет 2FA пользователя 79990000006 — как в check-login-methods.sh (в README — он же в Base32).
OTP_SECRET=sandbox-otp-test-secret

require_service
[ "$(curl -s -o /dev/null -w '%{http_code}' "$STUB/health")" = 200 ] || { echo "Заглушка не отвечает на $STUB/health (docker compose up -d idp-stub)"; exit 1; }
TOKEN=$(master_token)
[ -n "$TOKEN" ] || { echo "Нет токена мастер-админки"; exit 1; }

# admin <метод> <путь> [json] — Admin API realm; печатает ответ
admin() {
  curl -s -X "$1" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
    ${3:+--data "$3"} "$KC/admin/realms/$REALM$2"
}
# user_id <логин> — id пользователя (пусто — нет такого)
user_id() { admin GET "/users?username=$1&exact=true" | python3 -c 'import json,sys; u=json.load(sys.stdin); print(u[0]["id"] if u else "")'; }
# user <логин> <python-выражение над u> — поле пользователя
user() { admin GET "/users?username=$1&exact=true&briefRepresentation=false" | python3 -c "import json,sys; u=json.load(sys.stdin)[0]; print($2)"; }
# links <логин> — привязанные внешние входы: «провайдер:id» через пробел
links() {
  admin GET "/users/$(user_id "$1")/federated-identity" |
    python3 -c 'import json,sys; print(" ".join(sorted(l["identityProvider"] + ":" + l["userId"] for l in json.load(sys.stdin))))'
}

# ext_login <jar> <провайдер> — /auth/login → страница входа → кнопка провайдера → страница «войти как» заглушки
# ($WORK/stub.html). Печатает, куда отправил Keycloak (адрес провайдера).
ext_login() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html" url
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=/me/"
  curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" "$(header "$h" location)"
  url=$(kc "$page" "next(p['loginUrl'] for p in c['social']['providers'] if p['alias'] == '$2')")
  [ -n "$url" ] || { echo "нет кнопки $2"; return; }
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$KC$url"
  url=$(header "$h" location)
  curl -s -o "$WORK/stub.html" "$url"
  echo "$url"
}

# ext_pick <jar> <человек|deny> — выбрать человека на странице заглушки (или «Отказаться») и пройти назад через
# Keycloak. Печатает «→ <куда вернул сервис>» или «страница <шаблон Keycloak>[: сообщение]» ($WORK/page.html).
ext_pick() {
  local jar=$1 link
  if [ "$2" = deny ]; then
    link=$(grep -o 'class="deny" href="[^"]*"' "$WORK/stub.html" | sed 's/.*href="//; s/"$//; s/&amp;/\&/g')
  else
    link=$(grep -o "data-key=\"$2\" href=\"[^\"]*\"" "$WORK/stub.html" | sed 's/.*href="//; s/"$//; s/&amp;/\&/g')
  fi
  [ -n "$link" ] || { echo "на странице заглушки нет «$2»: $(head -c 200 "$WORK/stub.html")"; return; }
  ext_follow "$jar" "$STUB$link"
}

# ext_follow <jar> <адрес> — идти по редиректам: страница Keycloak — стоп; callback сервиса — вход завершён
ext_follow() {
  local jar=$1 url=$2 h="$WORK/h" page="$WORK/page.html" i template
  for i in $(seq 1 10); do
    case "$url" in /*) url="$KC$url" ;; esac
    if [[ "$url" == "$API/auth/callback"* ]]; then
      curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$url"
      echo "→ $(header "$h" location)"
      return
    fi
    curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" "$url"
    url=$(header "$h" location)
    if [ -z "$url" ]; then
      template=$(kc_page "$page")
      echo "страница ${template:-без kcContext ($(head -c 120 "$page"))}$(feedback "$page" | sed 's/^./: &/')"
      return
    fi
  done
  echo "слишком много редиректов"
}

# submit <jar> <поля curl…> — отправить форму текущей страницы Keycloak и пройти дальше
submit() {
  local jar=$1 action h="$WORK/h"
  shift
  action=$(form_action "$WORK/page.html")
  curl -s -o "$WORK/page.html" -D "$h" -b "$jar" -c "$jar" "$@" "$action"
  local next; next=$(header "$h" location)
  if [ -z "$next" ]; then echo "страница $(kc_page "$WORK/page.html")$(feedback "$WORK/page.html" | sed 's/^./: &/')"; return; fi
  ext_follow "$jar" "$next"
}

me() { curl -s -b "$1" "$API/me/" | python3 -c "import json,sys; r=json.load(sys.stdin)['result']; print($2)"; }
logout() { status "$1" POST /auth/logout > /dev/null; }

echo "0. С начала: учётные записи прошлых прогонов"
for login in 79990000101 79990000102 yandex-1130000003 esia-1000000102; do
  id=$(user_id "$login"); [ -n "$id" ] && admin DELETE "/users/$id" > /dev/null
done
CUSTOMER=$(user_id 79990000001)
admin DELETE "/users/$CUSTOMER/federated-identity/yandex" > /dev/null
admin DELETE "/users/$(user_id 79990000006)/federated-identity/yandex" > /dev/null
check "покупатель 79990000001 без привязок" "$(links 79990000001)" ""

echo "1. Страница входа: «Или войдите через»"
JAR="$WORK/jar"; : > "$JAR"
ext_login "$JAR" esia > /dev/null
check "кнопки: Госуслуги, Яндекс ID — по порядку" \
  "$(kc "$WORK/page.html" "', '.join(p['displayName'] for p in c['social']['providers'])")" "Госуслуги, Яндекс ID"

echo "2. Госуслуги: новый человек"
URL=$(ext_login "$JAR" esia)
Q=$(python3 -c 'import sys,urllib.parse as u; q=dict(u.parse_qsl(u.urlparse(sys.argv[1]).query)); print(q.get("state",""), q.get("timestamp",""), q.get("access_type",""), sep="|")' "$URL")
check "запрос в ЕСИА: state — UUID" "$(echo "$Q" | cut -d'|' -f1 | grep -cE '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')" 1
check "запрос в ЕСИА: timestamp «yyyy.MM.dd HH:mm:ss +0300»" "$(echo "$Q" | cut -d'|' -f2 | grep -cE '^[0-9]{4}\.[0-9]{2}\.[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2} \+0300$')" 1
check "заглушка приняла подпись запроса (страница «войти как»)" "$(grep -c 'data-key="new"' "$WORK/stub.html")" 1
check "вход → /me/ (обмен кода с подписью принят)" "$(ext_pick "$JAR" new)" "→ /me/"
check "/me/: телефон из Госуслуг" "$(me "$JAR" 'r["phone"]')" "+79990000102"
check "логин — телефон цифрами" "$(user 79990000102 'u["username"]')" 79990000102
check "атрибут phone" "$(user 79990000102 'u["attributes"]["phone"][0]')" "+79990000102"
check "почта из Госуслуг, подтверждена" "$(user 79990000102 'u["email"], u["emailVerified"]')" "maria.esia@sandbox.test True"
check "имя и фамилия" "$(user 79990000102 'u["firstName"], u["lastName"]')" "Мария Госуслугова"
check "привязка esia:<oid>" "$(links 79990000102)" "esia:1000000101"

echo "3. Повторный вход через Госуслуги — та же учётная запись, сразу"
logout "$JAR"
ext_login "$JAR" esia > /dev/null
check "вход → /me/ без вопросов" "$(ext_pick "$JAR" new)" "→ /me/"
check "/me/: тот же человек" "$(me "$JAR" 'r["phone"]')" "+79990000102"
check "второй учётной записи нет" "$(admin GET "/users?search=79990000102" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)))')" 1

echo "4. Госуслуги: телефон не подтверждён — не логин и не привязка к чужой учётной записи"
JAR2="$WORK/jar2"; : > "$JAR2"
ext_login "$JAR2" esia > /dev/null
check "вход → /me/" "$(ext_pick "$JAR2" unverified)" "→ /me/"
check "новая учётная запись esia-<oid>, без телефона" "$(user esia-1000000102 'u["username"], "phone" in u.get("attributes", {})')" "esia-1000000102 False"
check "покупатель 79990000001 не привязан" "$(links 79990000001)" ""

echo "5. Яндекс ID: новый человек"
JAR3="$WORK/jar3"; : > "$JAR3"
URL=$(ext_login "$JAR3" yandex)
check "запрос в Яндекс: доступы login:info login:email login:default_phone" \
  "$(python3 -c 'import sys,urllib.parse as u; print(dict(u.parse_qsl(u.urlparse(sys.argv[1]).query)).get("scope"))' "$URL")" \
  "login:info login:email login:default_phone"
check "вход → /me/ (токен и профиль с «Authorization: OAuth»)" "$(ext_pick "$JAR3" new)" "→ /me/"
check "/me/: телефон из Яндекса" "$(me "$JAR3" 'r["phone"]')" "+79990000101"
check "почта, имя" "$(user 79990000101 'u["email"], u["emailVerified"], u["firstName"]')" "anna.yandex@sandbox.test True Анна"
check "привязка yandex:<id>" "$(links 79990000101)" "yandex:1130000001"

echo "6. Яндекс ID без телефона"
JAR4="$WORK/jar4"; : > "$JAR4"
ext_login "$JAR4" yandex > /dev/null
check "вход → /me/" "$(ext_pick "$JAR4" nophone)" "→ /me/"
check "логин yandex-<id>, почта" "$(user yandex-1130000003 'u["username"], u["email"]')" "yandex-1130000003 nophone.yandex@sandbox.test"

echo "7. Яндекс ID с телефоном существующего покупателя — привязка, подтверждённая паролем"
JAR5="$WORK/jar5"; : > "$JAR5"
ext_login "$JAR5" yandex > /dev/null
check "«Аккаунт уже есть — привязать?»" "$(ext_pick "$JAR5" existing | cut -d: -f1)" "страница login-idp-link-confirm.ftl"
check "«Привязать» → пароль от учётной записи" "$(submit "$JAR5" --data-urlencode submitAction=linkAccount | cut -d: -f1)" "страница login.ftl"
check "форма — только пароль (логин известен)" "$(kc "$WORK/page.html" 'c.get("usernameHidden")')" True
check "неверный пароль — не привязали" "$(submit "$JAR5" --data-urlencode password=wrong | cut -d: -f1)" "страница login.ftl"
check "верный пароль → /me/" "$(submit "$JAR5" --data-urlencode "password=$PASSWORD")" "→ /me/"
check "/me/: это покупатель 79990000001" "$(me "$JAR5" 'r["phone"]')" "+79990000001"
check "привязка yandex на покупателе" "$(links 79990000001)" "yandex:1130000002"
logout "$JAR5"
ext_login "$JAR5" yandex > /dev/null
check "следующий вход через Яндекс — сразу покупатель" "$(ext_pick "$JAR5" existing; me "$JAR5" 'r["phone"]')" "→ /me/
+79990000001"

echo "8. 2FA не обходится: у пользователя с 2FA после внешнего входа — код"
JAR7="$WORK/jar7"; : > "$JAR7"
ext_login "$JAR7" yandex > /dev/null
ext_pick "$JAR7" otp > /dev/null
submit "$JAR7" --data-urlencode submitAction=linkAccount > /dev/null
check "привязка: пароль → код из приложения-аутентификатора" "$(submit "$JAR7" --data-urlencode "password=$PASSWORD" | cut -d: -f1)" "страница login-otp.ftl"
check "код → /me/" "$(submit "$JAR7" --data-urlencode "otp=$(fresh_totp "$OTP_SECRET")")" "→ /me/"
check "привязка yandex на пользователе с 2FA" "$(links 79990000006)" "yandex:1130000004"
logout "$JAR7"
ext_login "$JAR7" yandex > /dev/null
check "следующий вход через Яндекс — снова код" "$(ext_pick "$JAR7" otp | cut -d: -f1)" "страница login-otp.ftl"

echo "9. Отказ у провайдера"
JAR6="$WORK/jar6"; : > "$JAR6"
ext_login "$JAR6" yandex > /dev/null
check "«Отказаться» в Яндексе → снова форма входа" "$(ext_pick "$JAR6" deny | cut -d: -f1)" "страница login.ftl"

echo "10. Доступ к приложению закрыт — внешний вход его не обходит"
BFF=$(admin GET "/clients?clientId=sandbox-bff" | python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])')
DENIED=$(admin GET "/clients/$BFF/roles/access-denied")
MARIA=$(user_id 79990000102)
admin POST "/users/$MARIA/role-mappings/clients/$BFF" "[$DENIED]" > /dev/null
logout "$JAR"
ext_login "$JAR" esia > /dev/null
check "Госуслуги → «Доступ закрыт»" "$(ext_pick "$JAR" new)" \
  "страница error.ftl: Доступ к этому приложению для вас закрыт. Обратитесь к администратору приложения."
check "cookie сервиса не выданы → /me/ 401" "$(status "$JAR" GET /me/)" 401
admin DELETE "/users/$MARIA/role-mappings/clients/$BFF" "[$DENIED]" > /dev/null

echo "11. Заглушка отклоняет неверную подпись (проверка самой проверки)"
check "ЕСИА: client_secret не сходится → 400" \
  "$(curl -s -o /dev/null -w '%{http_code}' -G "$STUB/esia/aas/oauth2/v2/ac" --data-urlencode client_id=SANDBOX \
      --data-urlencode client_secret=bad --data-urlencode redirect_uri=http://x --data-urlencode scope=openid \
      --data-urlencode response_type=code --data-urlencode state=3550be91-6535-4e77-804b-8521fea853a3 \
      --data-urlencode 'timestamp=2026.10.05 20:30:00 +0300' --data-urlencode client_certificate_hash=sandbox-certificate-hash)" 400

summary
