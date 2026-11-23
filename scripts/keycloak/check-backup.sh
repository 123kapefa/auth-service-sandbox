#!/usr/bin/env bash
# Проверка этапа 13.5: резервные копии обеих баз и восстановление из них на стенде — полный цикл, как при настоящей аварии.
#  1. Админ проекта выдаёт ключ сервиса «A» (запись в базе сервиса + клиент в Keycloak).
#  2. Настоящий скрипт копий (deploy/backup/backup.sh — тот же, что у CronJob и сервиса auth-backup) снимает копию обеих баз в
#     одноразовом контейнере: пара файлов, суммы, права 0600, срок хранения (старые удаляются, самые свежие остаются).
#  3. После копии выдаётся ключ «B» — его в копии нет, после восстановления его не должно быть.
#  4. «Авария»: Keycloak и сервис останавливаются, обе базы и их тома удаляются, базы создаются заново пустыми.
#  5. scripts/backup/restore.sh возвращает базы из копии. Проверяется: ключ A на месте и по его секрету Keycloak выдаёт токен,
#     ключа B нет (восстановлено состояние на момент копии, а не «как было»), потом проходит check-stand.sh целиком.
# Нужны: стенд (docker compose up -d --wait) и разрешение на регистрацию ключей (scripts/keycloak/registration-token.sh --write-env).
# Запуск: scripts/keycloak/check-backup.sh   (разрушает и восстанавливает базы стенда: всё, что в них было после копии, пропадает)
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

KC="${KC:-http://localhost:${KEYCLOAK_PORT:-8180}}"
TOKEN_URL="$KC/realms/sandbox/protocol/openid-connect/token"
KEY_A=check-backup-a
KEY_B=check-backup-b
dc() { docker compose --project-directory "$ROOT" "$@"; }
env_value() { { grep -E "^$1=" "$ROOT/.env" 2> /dev/null || true; } | tail -1 | cut -d= -f2-; }

admin_call() {
  local args=(-s -o "$WORK/r.json" -w '%{http_code}' -b "$1" -c "$1" -X "$2")
  [ -n "${4:-}" ] && args+=(-H 'Content-Type: application/json' -d "$4")
  printf '%s ' "$(curl "${args[@]}" "$API$3")"
  python3 -c '
import json,sys
t=open(sys.argv[1]).read().strip(); d=json.loads(t) if t else {}
print(json.dumps(d["result"], ensure_ascii=False) if d.get("result") is not None else (d.get("error") or {}).get("messages", [{}])[0].get("code", ""))' "$WORK/r.json"
}
secret_of() { python3 -c 'import json,sys; print(json.loads(sys.argv[1].split(" ",1)[1])["clientSecret"])' "$1"; }
# service_token <client_id> <секрет> — «да», если Keycloak выдал токен, иначе его ошибка
service_token() {
  command curl -s -d grant_type=client_credentials -d "client_id=$1" --data-urlencode "client_secret=$2" "$TOKEN_URL" |
    python3 -c 'import json,sys; d=json.load(sys.stdin); print("да" if "access_token" in d else d.get("error"))'
}
keys_listed() { admin_call "$1" GET /auth/admin/services | python3 -c '
import json,sys
items=json.loads(sys.stdin.read().split(" ",1)[1])
print(" ".join(sorted(x["code"] for x in items if x["code"].startswith("check-backup"))))'; }

require_service
BACKUPS="$WORK/backups"; mkdir -p "$BACKUPS"
DISASTER=""; RESTORED=""
cleanup() {
  # Упали посреди аварии — возвращаем базы из копии, чтобы не оставить стенд без данных.
  if [ -n "$DISASTER" ] && [ -z "$RESTORED" ] && ls "$BACKUPS"/auth-service-*.dump > /dev/null 2>&1; then
    "$ROOT/scripts/backup/restore.sh" --dir "$BACKUPS" --yes > /dev/null 2>&1
  fi
  local jar="$WORK/cleanup"; : > "$jar"
  if [ "$(login "$jar" /me/ 1234567890)" = /me/ ]; then
    for c in "$KEY_A" "$KEY_B"; do admin_call "$jar" DELETE "/auth/admin/services/$c" > /dev/null; done
  fi
  rm -rf "$WORK"
}
trap cleanup EXIT

echo "1. Ключ сервиса A (данные до копии)"
ADMIN="$WORK/admin"; : > "$ADMIN"
check "админ проекта вошёл" "$(login "$ADMIN" /me/ 1234567890)" /me/
for c in "$KEY_A" "$KEY_B"; do admin_call "$ADMIN" DELETE "/auth/admin/services/$c" > /dev/null; done # остаток прошлого прогона
BODY_A="{\"code\":\"$KEY_A\",\"name\":\"Проверка копий A\",\"permissions\":[\"service.users.read\"]}"
R=$(admin_call "$ADMIN" POST /auth/admin/services "$BODY_A")
check "ключ A выдан" "$(echo "$R" | cut -d' ' -f1)" 200
SECRET_A=$(secret_of "$R")
check "по ключу A Keycloak выдаёт токен" "$(service_token "service-$KEY_A" "$SECRET_A")" да

echo "2. Копия настоящим скриптом"
NETWORK=$(docker inspect -f '{{range $k, $_ := .NetworkSettings.Networks}}{{$k}} {{end}}' "$(dc ps -q postgres)" | awk '{print $1}')
export AUTH_DB_PASSWORD="$(env_value POSTGRES_PASSWORD)" KEYCLOAK_DB_PASSWORD="$(env_value KEYCLOAK_DB_PASSWORD)"
run_backup() {
  docker run --rm --network "$NETWORK" -e "BACKUP_OWNER=$(id -u):$(id -g)" \
    -v "$ROOT/deploy/backup/backup.sh:/backup.sh:ro" -v "$BACKUPS:/backups" \
    -e AUTH_DB_HOST=postgres -e "AUTH_DB_NAME=$(env_value POSTGRES_DB)" -e "AUTH_DB_USER=$(env_value POSTGRES_USER)" -e AUTH_DB_PASSWORD \
    -e KEYCLOAK_DB_HOST=keycloak-postgres -e KEYCLOAK_DB_PASSWORD \
    -e BACKUP_RETENTION_DAYS=14 -e BACKUP_KEEP_MIN=3 \
    postgres:17-alpine sh /backup.sh
}
# Старые копии (пять штук): срок хранения должен убрать их, оставив самые свежие.
for day in 01 02 03 04 05; do
  for name in auth-service keycloak; do
    echo "old" > "$BACKUPS/$name-202501${day}T030000Z.dump"; echo "x  $name-202501${day}T030000Z.dump" > "$BACKUPS/$name-202501${day}T030000Z.dump.sha256"
    touch -t "202501${day}0300" "$BACKUPS/$name-202501${day}T030000Z.dump" "$BACKUPS/$name-202501${day}T030000Z.dump.sha256"
  done
done
run_backup > "$WORK/backup.log" 2>&1
check "скрипт копий завершился успешно" "$?" 0
STAMP=$(ls -1t "$BACKUPS"/auth-service-*.dump | head -1 | sed -E 's#.*/auth-service-(.*)\.dump#\1#')
check "метка копии — время UTC" "$(echo "$STAMP" | grep -cE '^[0-9]{8}T[0-9]{6}Z$')" 1
check "обе копии новой пары на месте, с суммами" \
  "$(for f in "auth-service-$STAMP.dump" "keycloak-$STAMP.dump"; do [ -s "$BACKUPS/$f" ] && [ -s "$BACKUPS/$f.sha256" ] && printf 1; done)" 11
check "суммы совпадают" "$(cd "$BACKUPS" && { sha256sum -c "auth-service-$STAMP.dump.sha256" "keycloak-$STAMP.dump.sha256" 2> /dev/null || shasum -a 256 -c "auth-service-$STAMP.dump.sha256" "keycloak-$STAMP.dump.sha256" 2> /dev/null; } | grep -c ': OK')" 2
check "права копий — только владелец (0600)" "$(ls -l "$BACKUPS/auth-service-$STAMP.dump" "$BACKUPS/keycloak-$STAMP.dump" | awk '{print substr($1,1,10)}' | sort -u)" "-rw-------"
check "копия читается pg_restore (в ней есть таблицы)" \
  "$(docker run --rm -v "$BACKUPS:/b:ro" postgres:17-alpine sh -c "pg_restore --list /b/auth-service-$STAMP.dump | grep -c 'TABLE DATA'" | tr -d '\r' | awk '{ print ($1 > 0) ? "да" : "нет" }')" да
check "срок хранения: из пяти старых остались две самые свежие (всего три копии каждой базы)" \
  "$(ls "$BACKUPS"/auth-service-*.dump | wc -l | tr -d ' ') $(ls "$BACKUPS"/keycloak-*.dump | wc -l | tr -d ' ')" "3 3"
check "самые старые удалены вместе с суммами" "$(ls "$BACKUPS"/*20250101* "$BACKUPS"/*20250102* "$BACKUPS"/*20250103* 2> /dev/null | wc -l | tr -d ' ')" 0
check "last-success записан" "$([ -s "$BACKUPS/last-success" ] && echo да || echo нет)" да
check "в журнале скрипта нет пароля" "$(grep -c -F -e "$AUTH_DB_PASSWORD" -e "$KEYCLOAK_DB_PASSWORD" "$WORK/backup.log")" 0
# Вторая копия подряд: новая пара, а срок хранения и минимум (три самые свежие) соблюдаются.
run_backup > /dev/null 2>&1
check "повторная копия: срок и минимум соблюдаются (три самые свежие копии каждой базы)" "$(ls "$BACKUPS"/auth-service-*.dump | wc -l | tr -d ' ')" 3
# Неверный пароль — копия не создаётся, прежние на месте, код выхода не 0.
BEFORE=$(ls "$BACKUPS" | wc -l | tr -d ' ')
KEYCLOAK_DB_PASSWORD=wrong run_backup > /dev/null 2>&1
check "неверный пароль: скрипт падает" "$([ $? -ne 0 ] && echo да || echo нет)" да
check "и не оставляет недописанных файлов" "$(ls "$BACKUPS" | grep -c '\.part$')" 0
check "и пары без второй копии: каталог тот же, что до неудачи" "$(ls "$BACKUPS" | wc -l | tr -d ' ')" "$BEFORE"
STAMP=$(ls -1t "$BACKUPS"/auth-service-*.dump | head -1 | sed -E 's#.*/auth-service-(.*)\.dump#\1#')

# Копии не получились — «аварию» не устраиваем: восстанавливать было бы нечего.
if [ "$FAIL" -ne 0 ]; then
  echo "Копия не снята как надо — базы не трогаем."
  summary
  exit 1
fi

echo "3. Ключ сервиса B (после копии: в копии его нет)"
BODY_B="{\"code\":\"$KEY_B\",\"name\":\"Проверка копий B\",\"permissions\":[\"service.users.read\"]}"
R=$(admin_call "$ADMIN" POST /auth/admin/services "$BODY_B")
check "ключ B выдан" "$(echo "$R" | cut -d' ' -f1)" 200
SECRET_B=$(secret_of "$R")
check "в списке — A и B" "$(keys_listed "$ADMIN")" "$KEY_A $KEY_B"

echo "4. Авария: базы удалены вместе с томами"
VOLUMES=$(for svc in postgres keycloak-postgres; do
  docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Name}}{{end}}{{end}}' "$(dc ps -q "$svc")"
done)
DISASTER=1
dc stop auth-service keycloak > /dev/null 2>&1
dc rm -sf postgres keycloak-postgres > /dev/null 2>&1
# shellcheck disable=SC2086
docker volume rm $VOLUMES > /dev/null 2>&1
check "тома баз удалены" "$(docker volume ls -q | grep -cxF -e "$(echo "$VOLUMES" | sed -n 1p)" -e "$(echo "$VOLUMES" | sed -n 2p)")" 0
dc up -d --wait postgres keycloak-postgres > /dev/null 2>&1
check "базы созданы заново пустыми (таблиц сервиса нет)" \
  "$(dc exec -T postgres psql -U "$(env_value POSTGRES_USER)" -d "$(env_value POSTGRES_DB)" -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'auth'" | tr -d '\r')" 0

echo "5. Восстановление из копии $STAMP"
check "без --yes скрипт ничего не трогает" "$("$ROOT/scripts/backup/restore.sh" --dir "$BACKUPS" --stamp "$STAMP" > /dev/null 2>&1; echo $?)" 2
CORRUPT="$WORK/corrupt"; mkdir -p "$CORRUPT"; cp "$BACKUPS/auth-service-$STAMP.dump"* "$BACKUPS/keycloak-$STAMP.dump"* "$CORRUPT/"
printf 'x' >> "$CORRUPT/keycloak-$STAMP.dump"
check "повреждённая копия (сумма не совпала) отклоняется до остановки сервисов" \
  "$("$ROOT/scripts/backup/restore.sh" --dir "$CORRUPT" --stamp "$STAMP" --yes > /dev/null 2>&1; echo $?)" 1
"$ROOT/scripts/backup/restore.sh" --dir "$BACKUPS" --stamp "$STAMP" --yes > "$WORK/restore.log" 2>&1
check "восстановление завершилось успешно" "$?" 0
RESTORED=1
[ -n "$(tail -1 "$WORK/restore.log" | grep Готово)" ] || sed 's/^/    /' "$WORK/restore.log" | tail -15

echo "6. Что вернулось"
for _ in $(seq 60); do [ "$(command curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")" = 200 ] && break; sleep 1; done
check "сервис готов" "$(command curl -s -o /dev/null -w '%{http_code}' "$API/health/ready")" 200
ADMIN2="$WORK/admin2"; : > "$ADMIN2"
check "админ проекта входит (люди и пароли из копии Keycloak)" "$(login "$ADMIN2" /me/ 1234567890)" /me/
check "в списке ключей — только A (B выдан после копии и пропал)" "$(keys_listed "$ADMIN2")" "$KEY_A"
check "клиент A в Keycloak вернулся: по прежнему секрету выдаётся токен" "$(service_token "service-$KEY_A" "$SECRET_A")" да
check "клиента B в Keycloak нет: токен не выдаётся" "$([ "$(service_token "service-$KEY_B" "$SECRET_B")" = да ] && echo да || echo нет)" нет

echo "7. check-stand.sh против восстановленных баз"
if "$ROOT/scripts/keycloak/check-stand.sh" > "$WORK/check-stand.log" 2>&1; then
  ok "check-stand: $(tail -1 "$WORK/check-stand.log")"
else
  bad "check-stand после восстановления"; tail -15 "$WORK/check-stand.log" | sed 's/^/    /'
fi

summary
