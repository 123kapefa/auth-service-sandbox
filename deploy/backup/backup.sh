#!/bin/sh
# Резервная копия обеих баз сервиса входа: базы сервиса (журнал, ключи сервисов, устройства) и базы Keycloak (проекты, люди,
# сессии). Один запуск = одна пара файлов с общей меткой времени: копия Keycloak восстанавливается только вместе с копией сервиса
# того же запуска (docs/migrations.md, «Резервные копии»). Тот же скрипт запускает CronJob чарта и сервис auth-backup в Compose.
#
# Настройки — переменные окружения (пароли — только переменными, не аргументами: аргументы видны в списке процессов):
#   AUTH_DB_HOST, AUTH_DB_PORT (5432), AUTH_DB_NAME (auth_service), AUTH_DB_USER (auth_service), AUTH_DB_PASSWORD
#   KEYCLOAK_DB_HOST, KEYCLOAK_DB_PORT (5432), KEYCLOAK_DB_NAME (keycloak), KEYCLOAK_DB_USER (keycloak), KEYCLOAK_DB_PASSWORD
#   BACKUP_DIR (/backups)            — куда класть копии
#   BACKUP_RETENTION_DAYS (14)       — копии старше этого срока удаляются…
#   BACKUP_KEEP_MIN (3)              — …но самые свежие BACKUP_KEEP_MIN копий каждой базы остаются всегда: если копии перестали получаться,
#                                      срок не должен вычистить последние рабочие
#   BACKUP_OWNER (не задана)         — user:group, которому передаются новые файлы: нужно, когда скрипт идёт от root, а копии читает
#                                      другой пользователь хоста (проверка check-backup.sh)
# Две базы снимаются одна за другой (секунды–минуты разницы), а не в один момент: запись, сделанная между ними, окажется в одной базе и не
# окажется в другой (например, ключ сервиса: строка в базе сервиса и клиент в Keycloak). Снимайте в тихое время; после восстановления
# проверьте список ключей сервисов в админке (docs/migrations.md).
# Файлы: <auth-service|keycloak>-<UTC YYYYmmddTHHMMSSZ>.dump (формат custom, pg_restore) и рядом .sha256. Копию сначала пишут в .part,
# проверяют (pg_restore --list), считают сумму и только потом переименовывают: недописанного файла под настоящим именем не бывает.
# После успеха обновляется файл last-success (время последней удачной копии — для проверки «копии идут»). Права — 0600: в копиях
# хэши паролей, телефоны и адреса почты людей.
set -eu
umask 077

BACKUP_DIR=${BACKUP_DIR:-/backups}
BACKUP_DIR=${BACKUP_DIR%/} # без хвостового «/»: иначе пути ls и find расходятся и защита свежих копий не срабатывает
RETENTION_DAYS=${BACKUP_RETENTION_DAYS:-14}
KEEP_MIN=${BACKUP_KEEP_MIN:-3}

case "$RETENTION_DAYS$KEEP_MIN" in
  *[!0-9]* | "") echo "BACKUP_RETENTION_DAYS и BACKUP_KEEP_MIN — целые числа" >&2; exit 2 ;;
esac
[ "$KEEP_MIN" -ge 1 ] || { echo "BACKUP_KEEP_MIN — не меньше 1" >&2; exit 2; }
mkdir -p "$BACKUP_DIR"
[ -w "$BACKUP_DIR" ] || { echo "В каталог $BACKUP_DIR нельзя писать" >&2; exit 1; }

# Недописанные файлы после жёсткого убийства (SIGKILL, вытеснение пода): trap в этом случае не срабатывает.
find "$BACKUP_DIR" -maxdepth 1 -name '*.part' -mmin +1440 -exec rm -f {} + 2> /dev/null || true

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
# Метка занята (две копии в одну секунду): ждём следующую секунду, иначе вторая затёрла бы и при сбое удалила первую.
while [ -e "$BACKUP_DIR/auth-service-$STAMP.dump" ] || [ -e "$BACKUP_DIR/keycloak-$STAMP.dump" ]; do
  sleep 1
  STAMP=$(date -u +%Y%m%dT%H%M%SZ)
done
PART=""
# Пара целая или никакой: сбой на второй базе убирает и готовую копию первой (иначе в каталоге остаётся копия без пары).
PAIR_DONE=""
finish() {
  status=$?
  [ -z "$PART" ] || rm -f "$PART"
  # После того как обе копии готовы, пару не трогаем: сбой chown или last-success не должен стирать снятую копию.
  if [ "$status" -ne 0 ] && [ -z "$PAIR_DONE" ]; then
    rm -f "$BACKUP_DIR"/auth-service-"$STAMP".dump "$BACKUP_DIR"/auth-service-"$STAMP".dump.sha256 \
      "$BACKUP_DIR"/keycloak-"$STAMP".dump "$BACKUP_DIR"/keycloak-"$STAMP".dump.sha256
  fi
}
trap finish EXIT
trap 'exit 1' INT TERM

# dump <метка файла> <host> <port> <db> <user> <пароль>
dump() {
  name=$1
  file="$BACKUP_DIR/$name-$STAMP.dump"
  PART="$file.part"
  PGPASSWORD=$6 pg_dump --host "$2" --port "$3" --username "$5" --dbname "$4" \
    --format=custom --no-owner --no-privileges --file "$PART"
  [ -s "$PART" ] || { echo "$name: копия пустая" >&2; exit 1; }
  pg_restore --list "$PART" > /dev/null || { echo "$name: копия не читается pg_restore" >&2; exit 1; }
  mv "$PART" "$file"
  PART=""
  (cd "$BACKUP_DIR" && sha256sum "$name-$STAMP.dump" > "$name-$STAMP.dump.sha256")
  [ -z "${BACKUP_OWNER:-}" ] || chown "$BACKUP_OWNER" "$file" "$file.sha256"
  echo "$name: $file ($(wc -c < "$file") байт)"
}

dump auth-service "${AUTH_DB_HOST:?нужен AUTH_DB_HOST}" "${AUTH_DB_PORT:-5432}" "${AUTH_DB_NAME:-auth_service}" \
  "${AUTH_DB_USER:-auth_service}" "${AUTH_DB_PASSWORD:?нужен AUTH_DB_PASSWORD}"
dump keycloak "${KEYCLOAK_DB_HOST:?нужен KEYCLOAK_DB_HOST}" "${KEYCLOAK_DB_PORT:-5432}" "${KEYCLOAK_DB_NAME:-keycloak}" \
  "${KEYCLOAK_DB_USER:-keycloak}" "${KEYCLOAK_DB_PASSWORD:?нужен KEYCLOAK_DB_PASSWORD}"

PAIR_DONE=1

# Срок хранения — только после того, как обе новые копии готовы.
for name in auth-service keycloak; do
  # Самые свежие KEEP_MIN копий не трогаем; из остальных удаляем старше срока.
  keep=$(ls -1t "$BACKUP_DIR"/"$name"-*.dump 2> /dev/null | head -n "$KEEP_MIN")
  find "$BACKUP_DIR" -maxdepth 1 -name "$name-*.dump" -mtime +"$RETENTION_DAYS" | while IFS= read -r old; do
    case "
$keep
" in
      *"
$old
"*) ;;
      *) rm -f "$old" "$old.sha256"; echo "удалена по сроку: $old" ;;
    esac
  done
done

date -u +%Y-%m-%dT%H:%M:%SZ > "$BACKUP_DIR/last-success"
[ -z "${BACKUP_OWNER:-}" ] || chown "$BACKUP_OWNER" "$BACKUP_DIR/last-success"
echo "готово: $STAMP"
