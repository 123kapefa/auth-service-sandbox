#!/usr/bin/env bash
# Проверка этапа 10.6 (docs/keycloak/10-passkey-offer.md): после входа по коду — «Входите без кода» (завести ключ
# доступа); «Не сейчас» — дальше в сервис, и 30 дней не спрашиваем; после входа паролем не предлагаем. Не получилось
# завести ключ — «Попробовать ещё раз» или «Не сейчас». Сам ключ (окно браузера) curl не заведёт — это проверено в
# браузере с виртуальным ключом.
# Проверка заводит своего пользователя и удаляет его в конце.
# Запуск: стенд поднят (Docker: сервис :8080, Keycloak :8180, заглушка телефонии :8190), затем
#   scripts/keycloak/check-passkey-offer.sh
# Стенд, поднятый до 10.6: scripts/project/enable-passkey-offer.py sandbox
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
REALM=sandbox
PHONE=79990000081
ATTRIBUTE=passkey_offer_skipped_at

admin_api() { curl -s -H "Authorization: Bearer $(master_token < /dev/null)" -H 'Content-Type: application/json' "$@"; }
user_id() { admin_api "$KC/admin/realms/$REALM/users?username=$1&exact=true" | python3 -c 'import json,sys; u=json.load(sys.stdin); print(u[0]["id"] if u else "")'; }
remove_user() { local id; id=$(user_id "$1"); [ -z "$id" ] || admin_api -X DELETE "$KC/admin/realms/$REALM/users/$id" > /dev/null; }
# skipped_at — когда нажали «Не сейчас» («—» — не нажимали)
skipped_at() {
  admin_api "$KC/admin/realms/$REALM/users/$(user_id "$PHONE")" |
    python3 -c 'import json,sys; print(((json.load(sys.stdin).get("attributes") or {}).get(sys.argv[1]) or ["—"])[0])' "$ATTRIBUTE"
}
# skipped_days_ago <дней> — будто «Не сейчас» нажали столько дней назад
skipped_days_ago() {
  local id; id=$(user_id "$PHONE")
  admin_api "$KC/admin/realms/$REALM/users/$id" | python3 -c '
import datetime,json,sys
u = json.load(sys.stdin)
at = datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(days=int(sys.argv[1]))
u.setdefault("attributes", {})[sys.argv[2]] = [at.strftime("%Y-%m-%dT%H:%M:%SZ")]
print(json.dumps(u))' "$1" "$ATTRIBUTE" | admin_api -X PUT -d @- "$KC/admin/realms/$REALM/users/$id"
}
trap 'remove_user "$PHONE"; rm -rf "$WORK"' EXIT

PAGE() { echo "$WORK/page.html"; }
# screen — что на странице: «предложение» (регистрация ключа с platformPasskeyOffer), «ошибка» (не получилось) или имя
screen() {
  kc "$(PAGE)" '("ошибка" if c["templateName"] == "webauthn-error.ftl" else "предложение") if get("platformPasskeyOffer") == "true" else c["templateName"]'
}
# post <jar> <поле=значение>… — отправить форму страницы; печатает экран Keycloak (по переадресациям внутри Keycloak),
# а если ушли в сервис — куда он вернул
post() {
  local jar=$1 action args=() url
  shift
  action=$(form_action "$(PAGE)")
  for kv in "$@"; do args+=(--data-urlencode "$kv"); done
  curl -s -o "$(PAGE)" -D "$WORK/h" -b "$jar" -c "$jar" "${args[@]}" "$action"
  url=$(header "$WORK/h" location)
  while [[ "$url" == "$KC"* ]]; do
    curl -s -o "$(PAGE)" -D "$WORK/h" -b "$jar" -c "$jar" "$url"
    url=$(header "$WORK/h" location)
  done
  if [ -n "$url" ]; then
    curl -s -o /dev/null -D "$WORK/h" -b "$jar" -c "$jar" "$url"
    header "$WORK/h" location
  else
    screen
  fi
}
# by_code <jar> — вход по коду на номер (звонок); печатает экран после кода или куда вернул сервис
by_code() {
  : > "$1"
  code_start_fresh "$1" "$PHONE" > /dev/null
  post "$1" "code=$(phone_code "$PHONE")"
}

require_service
WORK=$(mktemp -d)
remove_user "$PHONE"
J="$WORK/jar"

echo "1. Первый вход по коду"
check "новый покупатель по коду → «Входите без кода»" "$(by_code "$J")" предложение
check "на странице — всё для окна ключа (вызов, пользователь)" \
  "$(kc "$(PAGE)" '"есть" if c.get("challenge") and c.get("username") else "нет"')" есть
check "окно ключа закрыли или не вышло → «Не получилось»" "$(post "$J" "error=NotAllowedError")" ошибка
check "«Попробовать ещё раз» → снова предложение" "$(post "$J" "isSetRetry=retry")" предложение
check "«Не сейчас» → в сервис" "$(post "$J" "skip=true")" /me/
check "«Не сейчас» запомнен (сегодня)" "$(skipped_at | cut -c1-10)" "$(date -u +%Y-%m-%d)"

echo "2. Пауза 30 дней"
check "снова по коду на следующий день — без предложения" "$(skipped_days_ago 1; by_code "$J")" /me/
check "через 29 дней — без предложения" "$(skipped_days_ago 29; by_code "$J")" /me/
check "через 30 дней — снова предложение" "$(skipped_days_ago 30; by_code "$J")" предложение
check "«Не сейчас» → в сервис" "$(post "$J" "skip=true")" /me/

echo "3. Другие входы"
admin_api -X PUT -d "{\"type\":\"password\",\"value\":\"$PASSWORD\",\"temporary\":false}" \
  "$KC/admin/realms/$REALM/users/$(user_id "$PHONE")/reset-password" > /dev/null
skipped_days_ago 31
: > "$J"
check "вход паролем — без предложения" "$(login "$J" /me/ "$PHONE")" /me/
check "«Не сейчас» не нажимали — время прежнее (31 день назад)" \
  "$(skipped_at | cut -c1-10)" "$(date -u -d '31 days ago' +%Y-%m-%d)"

summary
