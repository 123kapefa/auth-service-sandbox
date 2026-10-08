#!/usr/bin/env bash
# Логи всех контейнеров Docker на машине (стенд, проекты, магазин, установка check-deploy) — в <каталог>/containers.
# Для CI: после упавших проверок вместе с выводом скриптов уходит в артефакт. Запуск:
#   scripts/ci/collect-logs.sh stand-logs
set -uo pipefail

OUT="${1:?каталог для логов}/containers"
mkdir -p "$OUT"
docker ps -a --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}' > "$OUT/ps.txt"
for id in $(docker ps -aq); do
  name=$(docker inspect --format '{{.Name}}' "$id" | tr -d /)
  docker logs --timestamps "$id" > "$OUT/$name.log" 2>&1
  docker inspect --format '{{json .State}}' "$id" > "$OUT/$name.state.json"
done
echo "Логи $(docker ps -aq | wc -l) контейнеров: $OUT"
