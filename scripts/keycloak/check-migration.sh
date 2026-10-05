#!/usr/bin/env bash
# Проверка этапа 6 (docs/keycloak/06-bcrypt-migration.md): перенос пользователей «боевого» сервиса с BCrypt-паролями.
# Готовит «боевую» базу (scripts/legacy/seed-legacy.sh), удаляет из Keycloak прошлый перенос, переносит заново
# (AuthService.LegacyImport — нужен .NET SDK) и входит перенесёнными пользователями через страницу Keycloak.
# Запуск: сервис, Keycloak и Postgres подняты, затем
#   scripts/keycloak/check-migration.sh
set -uo pipefail

# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
ADMIN="$KC/admin/realms/sandbox"
# Id пользователей из infra/legacy/seed.sql: 1e9ac700-…-00000000000N, пароль — Legacy-N! (у 9-го — «Пароль-9!»)
legacy_id() { echo "1e9ac700-0000-4000-8000-00000000000$1"; }

# import — прогон инструмента переноса; печатает итоговую строку отчёта (без чисел времени), код выхода — инструмента
import() {
  local log="$WORK/import.log" code
  DOTNET_ENVIRONMENT=Development dotnet run --project "$ROOT/src/AuthService.LegacyImport" --no-launch-profile > "$log" 2>&1
  code=$?
  cp "$log" "$WORK/last-import.log"
  sed -n 's/.*\(Перенос: .*\) — за .*/\1/p' "$log"
  return $code
}

# kc_user <id> <python-выражение над u> — поле пользователя Keycloak (Admin API, мастер-токен); нет пользователя — «нет»
kc_user() {
  curl -s -H "Authorization: Bearer $MASTER" "$ADMIN/users/$1" | python3 -c '
import json,sys
u=json.load(sys.stdin)
a=u.get("attributes") or {}
print("нет" if "id" not in u else eval(sys.argv[1]))' "$2"
}

# algorithm <id> — алгоритм хэша пароля пользователя (bcrypt — перенесённый, argon2 — после первого входа)
algorithm() {
  curl -s -H "Authorization: Bearer $MASTER" "$ADMIN/users/$1/credentials" | python3 -c '
import json,sys
c=[x for x in json.load(sys.stdin) if x["type"] == "password"]
print(json.loads(c[0]["credentialData"])["algorithm"] if c else "нет пароля")'
}

me() { curl -s -b "$1" "$API/me/" | python3 -c 'import json,sys; r=json.load(sys.stdin)["result"]; v=r[sys.argv[1]]; print(",".join(v) if isinstance(v,list) else v)' "$2" 2>/dev/null; }

require_service
command -v dotnet > /dev/null || { echo "Нужен .NET SDK: инструмент переноса запускается через dotnet run"; exit 1; }
MASTER=$(master_token)
[ -n "$MASTER" ] || { echo "Нет токена мастер-админки (KEYCLOAK_ADMIN_PASSWORD в .env)"; exit 1; }

echo "0. Подготовка: «боевая» база и чистый Keycloak"
"$ROOT/scripts/legacy/seed-legacy.sh" > /dev/null
for n in 1 2 3 4 5 6 7 8 9; do
  curl -s -o /dev/null -X DELETE -H "Authorization: Bearer $MASTER" "$ADMIN/users/$(legacy_id "$n")"
done
check "в Keycloak нет перенесённых с прошлого прогона" "$(kc_user "$(legacy_id 1)" 'u["id"]')" нет
dotnet build -v q "$ROOT/src/AuthService.LegacyImport" > /dev/null 2>&1 || { echo "Инструмент переноса не собрался"; exit 1; }

echo "1. Перенос"
check "отчёт: 8 перенесено, 1 конфликт" "$(import)" \
  "Перенос: прочитано 9, перенесено 8, уже были 0, конфликтов 1, ошибок 0, не переносятся 0"
check "в отчёте — конфликт с номером 79990000001" \
  "$(grep -c 'Конфликт: 1e9ac700-0000-4000-8000-000000000008 79990000001: номер занят пользователем Keycloak' "$WORK/last-import.log")" 1
check "в отчёте — сколько без пароля и с ролью courier" \
  "$(grep -cE 'без пароля — вход по коду: 1|роль «courier» не переносится.*: 1' "$WORK/last-import.log")" 2

echo "2. Что перенеслось"
U1=$(legacy_id 1)
check "Id тот же, что в боевой базе; логин — телефон цифрами" "$(kc_user "$U1" 'u["id"] + " " + u["username"]')" "$U1 79991110001"
check "атрибуты phone и active_mode" "$(kc_user "$U1" 'a["phone"][0] + " " + a["active_mode"][0]')" "+79991110001 customer"
check "дата создания — из боевой базы" "$(kc_user "$U1" 'u["createdTimestamp"]')" 1740819600000
check "пароль — BCrypt-хэш как есть" "$(algorithm "$U1")" bcrypt
check "без пароля (2) — без пароля" "$(algorithm "$(legacy_id 2)")" "нет пароля"
check "заблокированный (3) — выключен, с причиной" "$(kc_user "$(legacy_id 3)" 'str(u["enabled"]) + " " + a["restriction_reason"][0]')" \
  "False Подозрение на мошенничество"
check "курьер (6) — покупатель, исходная роль в legacy_role" "$(kc_user "$(legacy_id 6)" 'a["active_mode"][0] + " " + a["legacy_role"][0]')" \
  "customer courier"
check "конфликт (8) не перенесён" "$(kc_user "$(legacy_id 8)" 'u["id"]')" нет

echo "3. Вход старым паролем через страницу входа"
JAR="$WORK/jar1"; : > "$JAR"
check "покупатель (1): Legacy-1! → вход" "$(login "$JAR" /me/ 79991110001 'Legacy-1!')" /me/
check "вошёл он: телефон и роль customer" "$(me "$JAR" phone) $(me "$JAR" roles)" "+79991110001 customer"
check "после входа хэш заменён на argon2" "$(algorithm "$U1")" argon2
status "$JAR" POST /auth/logout > /dev/null
JAR="$WORK/jar1b"; : > "$JAR"
check "тот же пароль после замены хэша — вход" "$(login "$JAR" /me/ 79991110001 'Legacy-1!')" /me/
status "$JAR" POST /auth/logout > /dev/null
JAR="$WORK/jar1c"; : > "$JAR"
check "неверный пароль — отказ" "$(starts "$(login "$JAR" /me/ 79991110001 'Legacy-1?')" "keycloak: Неправильное имя пользователя или пароль")" \
  "keycloak: Неправильное имя пользователя или пароль"
JAR="$WORK/jar4"; : > "$JAR"
check "менеджер (4): вход, роли customer и manager" "$(login "$JAR" /me/ 79991110004 'Legacy-4!' > /dev/null; me "$JAR" roles)" "customer,manager"
status "$JAR" POST /auth/logout > /dev/null
JAR="$WORK/jar9"; : > "$JAR"
check "пароль кириллицей (9) — вход" "$(login "$JAR" /me/ 79991110009 'Пароль-9!')" /me/
status "$JAR" POST /auth/logout > /dev/null
JAR="$WORK/jar7"; : > "$JAR"
# Временная блокировка после неверных паролей в боевом сервисе не переносится: в Keycloak своя защита от перебора.
check "временно запертый в боевом сервисе (7) — входит" "$(login "$JAR" /me/ 79991110007 'Legacy-7!')" /me/
status "$JAR" POST /auth/logout > /dev/null
JAR="$WORK/jar3"; : > "$JAR"
check "заблокированный (3) — не входит" "$(starts "$(login "$JAR" /me/ 79991110003 'Legacy-3!')" "keycloak: Учетная запись заблокирована")" \
  "keycloak: Учетная запись заблокирована"

echo "4. Без пароля — вход по коду"
JAR="$WORK/jar2"; : > "$JAR"
check "экран кода для 79991110002" "$(code_start_fresh "$JAR" 79991110002)" "Вам звонят на +79991110002."
check "верный код → вход" "$(code_submit "$JAR" "$(phone_code 79991110002)")" /me/
check "вошёл перенесённый пользователь" "$(me "$JAR" phone)" "+79991110002"
status "$JAR" POST /auth/logout > /dev/null

echo "5. Повторный прогон"
check "отчёт: ничего нового, 8 уже были" "$(import)" \
  "Перенос: прочитано 9, перенесено 0, уже были 8, конфликтов 1, ошибок 0, не переносятся 0"
check "хэш после входа не перезаписан старым BCrypt" "$(algorithm "$U1")" argon2

summary
