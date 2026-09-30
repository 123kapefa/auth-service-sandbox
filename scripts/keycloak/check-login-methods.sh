#!/usr/bin/env bash
# Проверка этапа 3а (docs/keycloak/03a-login-methods.md): способы входа на странице Keycloak.
# Запуск: сервис, Keycloak и Mailpit подняты, затем
#   scripts/keycloak/check-login-methods.sh                            # Docker: http://localhost:8080
#   API=http://localhost:5080 scripts/keycloak/check-login-methods.sh    # Development: dotnet run
# Внимание: пароль пользователя 79990000005 (reset@sandbox.test) после прогона — новый, случайный.
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
MAILPIT="${MAILPIT:-http://localhost:${MAILPIT_UI_PORT:-8025}}"

# mail_count <адрес> — сколько писем пришло на адрес
mail_count() { curl -s "$MAILPIT/api/v1/search?query=to:$1" | python3 -c 'import json,sys; print(json.load(sys.stdin)["messages_count"])'; }

# mail_link <адрес> — ссылка action-token из последнего письма (ждём до 10 секунд)
mail_link() {
  local i link
  for i in 1 2 3 4 5 6 7 8 9 10; do
    link=$(curl -s "$MAILPIT/api/v1/search?query=to:$1" | python3 -c '
import json,sys,re,html,urllib.request
messages = json.load(sys.stdin)["messages"]
if messages:
    body = json.load(urllib.request.urlopen(sys.argv[1] + "/api/v1/message/" + messages[0]["ID"]))
    links = [html.unescape(l) for l in re.findall(r"href=\"([^\"]+)\"", body["HTML"] or "") if "action-token" in l]
    print(links[0] if links else "")' "$MAILPIT")
    [ -n "$link" ] && { echo "$link"; return; }
    sleep 1
  done
}

# reset_request <jar> <логин> — «Забыли пароль?» со страницы входа; печатает сообщение Keycloak
reset_request() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html" url action
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$API/auth/login?return_to=/me/"
  curl -s -o "$page" -b "$jar" -c "$jar" "$(header "$h" location)"
  url=$(python3 -c 'import re,sys,html; m=re.search(r"href=\"([^\"]*reset-credentials[^\"]*)\"", open(sys.argv[1]).read()); print(html.unescape(m.group(1)) if m else "")' "$page")
  [ -n "$url" ] || { echo "нет ссылки «Забыли пароль?»"; return; }
  curl -s -o "$page" -b "$jar" -c "$jar" "$KC$url"
  action=$(form_action "$page" kc-reset-password-form)
  curl -s -o "$page" -b "$jar" -c "$jar" --data-urlencode "username=$2" "$action"
  feedback "$page"
}

# reset_complete <jar> <ссылка из письма> <новый пароль> — смена пароля; печатает, куда сервис вернул браузер
reset_complete() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html" url
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$2"
  curl -s -o "$page" -b "$jar" -c "$jar" "$(header "$h" location)"
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" \
    --data-urlencode "password-new=$3" --data-urlencode "password-confirm=$3" "$(form_action "$page" kc-passwd-update-form)"
  url=$(header "$h" location)
  [ -n "$url" ] || { echo "пароль не сохранён"; return; }
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$url"
  header "$h" location
}

# totp <секрет> — текущий 6-значный код (RFC 6238: HMAC-SHA1, шаг 30 с), как у приложения-аутентификатора
totp() {
  python3 -c '
import hmac,hashlib,struct,sys,time
digest = hmac.new(sys.argv[1].encode(), struct.pack(">Q", int(time.time()) // 30), hashlib.sha1).digest()
offset = digest[-1] & 15
print("%06d" % ((struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7fffffff) % 1000000))' "$1"
}

# otp_login <jar> <логин> <код> — вход логином, паролем и кодом 2FA; печатает, куда сервис вернул браузер
otp_login() {
  local jar=$1 h="$WORK/h" page="$WORK/page.html" url
  url=$(login_until_done "$jar" /me/ "$2" "$PASSWORD") && { echo "код не спросили"; return; }
  grep -q 'kc-otp-login-form' "$page" || { echo "код не спросили ($url)"; return; }
  curl -s -o "$page" -D "$h" -b "$jar" -c "$jar" --data-urlencode "otp=$3" "$(form_action "$page" kc-otp-login-form)"
  url=$(header "$h" location)
  [ -n "$url" ] || { echo "keycloak: $(feedback "$page")"; return; }
  curl -s -o /dev/null -D "$h" -b "$jar" -c "$jar" "$url"
  header "$h" location
}

me_phone() { curl -s -b "$1" "$API/me/" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["phone"])' 2>/dev/null; }

require_service
[ "$(curl -s -o /dev/null -w '%{http_code}' "$MAILPIT/api/v1/messages")" = 200 ] || { echo "Mailpit не отвечает на $MAILPIT"; exit 1; }

echo "1. Вход по почте"
JAR="$WORK/jar"; : > "$JAR"
check "вход по почте customer@sandbox.test → возврат на return_to" "$(login "$JAR" /me/ customer@sandbox.test)" /me/
check "вошёл тот же пользователь, что по телефону" "$(me_phone "$JAR")" +79990000001
status "$JAR" POST /auth/logout > /dev/null

echo "2. Сброс пароля по почте"
curl -s -o /dev/null -X DELETE "$MAILPIT/api/v1/messages"
NEW_PASSWORD="Sandbox-$(date +%s)"
JAR="$WORK/jar2"; : > "$JAR"
check "запрос сброса принят" "$(reset_request "$JAR" 79990000005)" "В ближайшее время Вы должны получить письмо с дальнейшими инструкциями."
LINK=$(mail_link reset@sandbox.test)
check "письмо со ссылкой пришло на reset@sandbox.test" "$([ -n "$LINK" ] && echo да || echo нет)" да
check "новый пароль сохранён → вход и возврат на return_to" "$(reset_complete "$JAR" "$LINK" "$NEW_PASSWORD")" /me/
check "вошёл пользователь 79990000005" "$(me_phone "$JAR")" +79990000005
status "$JAR" POST /auth/logout > /dev/null
JAR="$WORK/jar3"; : > "$JAR"
check "вход с новым паролем" "$(login "$JAR" /me/ 79990000005 "$NEW_PASSWORD")" /me/
status "$JAR" POST /auth/logout > /dev/null
# Ссылка одноразовая: второй переход — 400 и никакой формы смены пароля.
check "повторно ссылка из письма не работает" \
  "$(curl -s -o "$WORK/p.html" -w '%{http_code}' "$LINK"; grep -q 'kc-passwd-update-form' "$WORK/p.html" && echo ' есть форма' || echo ' без формы')" "400 без формы"

echo "3. Сброс для неизвестного пользователя — ничего не раскрывает"
JAR="$WORK/jar4"; : > "$JAR"
check "тот же ответ, что для существующего" "$(reset_request "$JAR" 79990009999)" "В ближайшее время Вы должны получить письмо с дальнейшими инструкциями."
sleep 2
check "письмо не отправлено" "$(curl -s "$MAILPIT/api/v1/messages" | python3 -c 'import json,sys; print(json.load(sys.stdin)["messages_count"])')" 1

echo "4. 2FA по желанию пользователя"
JAR="$WORK/jar5"; : > "$JAR"
check "без настроенной 2FA код не спрашивают" "$(login "$JAR" /me/ 79990000001)" /me/
status "$JAR" POST /auth/logout > /dev/null
JAR="$WORK/jar6"; : > "$JAR"
R=$(otp_login "$JAR" 79990000006 000000)
check "с 2FA: неверный код отклонён — «$R»" "${R:0:9}" keycloak:
JAR="$WORK/jar7"; : > "$JAR"
check "с 2FA: после пароля спрашивают код, верный код → вход" "$(otp_login "$JAR" 79990000006 "$(totp sandbox-otp-test-secret)")" /me/
check "вошёл пользователь с 2FA" "$(me_phone "$JAR")" +79990000006
status "$JAR" POST /auth/logout > /dev/null

echo "5. Номер приводится к логину на сервере (форма получает номер как набрали, без скрипта темы)"
for PHONE in "+7 (999) 000-00-01" "8 999 000 00 01" "9990000001"; do
  JAR="$WORK/jar-phone"; : > "$JAR"
  check "«$PHONE» → вход" "$(login "$JAR" /me/ "$PHONE")" /me/
  check "«$PHONE» → пользователь 79990000001" "$(me_phone "$JAR")" +79990000001
  status "$JAR" POST /auth/logout > /dev/null
done
JAR="$WORK/jar-unknown"; : > "$JAR"
R=$(login "$JAR" /me/ "+7 999 000-99-99")
check "неизвестный номер — ошибка входа — «$R»" "${R:0:9}" keycloak:

summary
