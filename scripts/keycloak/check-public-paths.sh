#!/usr/bin/env bash
# Проверка закрытия мастер-админки (этап 12.12): что из Keycloak видно снаружи, за Ingress или прокси.
# Снаружи нужен только realm проекта (/realms/<код>) и /resources; realm master (вход мастер-админа, пароль которого
# иначе можно перебирать из интернета) и /admin — закрыты. Матрица обходов пути: «..», кодированные точки и слеши,
# двойное кодирование, двойной слеш, регистр, «;» (параметры пути), соседний realm с общим началом имени.
# Каждый путь шлётся как есть (curl --path-as-is): иначе curl сам «причёсывает» запрос и обход не проверяется.
# Запуск:
#   PUBLIC=https://id.shop.example.com REALM=shop scripts/keycloak/check-public-paths.sh
#   PUBLIC=http://127.0.0.1:8088 HOST_HEADER=id.test REALM=sandbox ...   (Ingress в kind: адрес узла + Host)
#   CURL_INSECURE=1 — не проверять сертификат (контроллер Ingress в kind отдаёт временный)
#   DIRECT=http://localhost:8180 — Keycloak напрямую, без прокси: контроль, что закрытые пути вообще существуют
#   (без него «закрыто» могло бы значить «такого пути нет»). CURL_CA_BUNDLE — корневой сертификат прокси.
set -uo pipefail

PUBLIC="${PUBLIC:?нужен PUBLIC — внешний адрес Keycloak (https://id.shop.example.com)}"
REALM="${REALM:?нужен REALM — код проекта}"
HOST_HEADER="${HOST_HEADER:-}"
DIRECT="${DIRECT:-}"
CURL_OPTS=""; [ -n "${CURL_INSECURE:-}" ] && CURL_OPTS="-k"
PASS=0; FAIL=0
ok()    { PASS=$((PASS+1)); printf '  \033[32m✓\033[0m %s\n' "$1"; }
bad()   { FAIL=$((FAIL+1)); printf '  \033[31m✗\033[0m %s\n' "$1"; }
check() { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1 (ожидали '$3', получили '$2')"; fi; }
summary() { echo; echo "Итого: $PASS ✓, $FAIL ✗"; [ "$FAIL" -eq 0 ]; }

WORK=$(mktemp -d); trap 'rm -rf "$WORK"' EXIT
# Пустой массив при set -u в bash 3.2 (macOS) — ошибка: Host подставляем строкой-условием.
get() { # get <база> <путь> [Host] — «код размер» и тело в $WORK/body
  local base=$1 path=$2 host=${3:-}
  if [ -n "$host" ]; then
    curl -s $CURL_OPTS --path-as-is -o "$WORK/body" -w '%{http_code}' -H "Host: $host" "$base$path" 2>/dev/null
  else
    curl -s $CURL_OPTS --path-as-is -o "$WORK/body" -w '%{http_code}' "$base$path" 2>/dev/null
  fi
}
public() { get "$PUBLIC" "$1" "$HOST_HEADER"; }
# Достижимость realm master: в теле — его issuer или страница входа мастер-админа.
reached_master() { grep -q -E '"realm":"master"|/realms/master"|Sign in to Keycloak|Administration Console' "$WORK/body" 2>/dev/null && echo да || echo нет; }

echo "1. Открыто: realm проекта и ресурсы"
check "/realms/$REALM/.well-known/openid-configuration" "$(public "/realms/$REALM/.well-known/openid-configuration")" 200
check "  issuer — realm проекта" "$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["issuer"].rsplit("/",1)[-1])' "$WORK/body" 2>/dev/null)" "$REALM"
check "/realms/$REALM/protocol/openid-connect/certs (ключи)" "$(public "/realms/$REALM/protocol/openid-connect/certs")" 200
# Ресурс темы — адрес берём со страницы входа, версия в нём меняется.
public "/realms/$REALM/protocol/openid-connect/auth?client_id=x&response_type=code&redirect_uri=http://localhost/" > /dev/null
RES=$(grep -o -E '/resources/[A-Za-z0-9_./-]+\.(css|js)' "$WORK/body" 2>/dev/null | head -1)
if [ -n "$RES" ]; then check "/resources (ресурс темы входа)" "$(public "$RES")" 200
else bad "на странице входа не нашлось ссылки на /resources"; fi

echo "2. Закрыто: realm master, мастер-админка и обходы пути"
# Для каждого пути: ответ — 4xx (не 2xx/3xx/5xx) и в теле нет realm master. 5xx тоже плохо: запрос дошёл до сервера.
CLOSED=(
  "/realms/master"
  "/realms/master/"
  "/realms/master/.well-known/openid-configuration"
  "/realms/master/protocol/openid-connect/auth?client_id=admin-cli&response_type=code"
  "/realms/$REALM/../master/.well-known/openid-configuration"
  "/realms/$REALM/%2e%2e/master/.well-known/openid-configuration"
  "/realms/$REALM/%2E%2E/master/.well-known/openid-configuration"
  "/realms/$REALM/.%2e/master/.well-known/openid-configuration"
  "/realms/$REALM%2f..%2fmaster/.well-known/openid-configuration"
  "/realms/$REALM/..%2fmaster/.well-known/openid-configuration"
  "/realms/$REALM/%2e%2e%2fmaster/.well-known/openid-configuration"
  "/realms/$REALM/%252e%252e/master/.well-known/openid-configuration"
  "/realms/$REALM/%252e%252e%252fmaster/.well-known/openid-configuration"
  "/realms//master/.well-known/openid-configuration"
  "/realms/$REALM//../master/.well-known/openid-configuration"
  "/realms/./master/.well-known/openid-configuration"
  "/REALMS/master/.well-known/openid-configuration"
  "/Realms/master/.well-known/openid-configuration"
  "/realms/MASTER/.well-known/openid-configuration"
  "/realms/$REALM;/..;/master/.well-known/openid-configuration"
  "/realms/$REALM;x=y/../master/.well-known/openid-configuration"
  "/realms/$REALM%3b/..%3b/master/.well-known/openid-configuration"
  "/realms/$REALM%00/../master/.well-known/openid-configuration"
  "/realms/$REALM/..;/master/.well-known/openid-configuration"
  "/realms/$REALM/..%3b/master/.well-known/openid-configuration"
  "/realms/$REALM/.%2e/master/.well-known/openid-configuration"
  "/realms/$REALM/%2e./master/.well-known/openid-configuration"
  "/realms/$REALM/..%5cmaster/.well-known/openid-configuration"
  "/realms/$REALM/%2e%2e%5cmaster/.well-known/openid-configuration"
  "/realms/$REALM/%c0%ae%c0%ae/master/.well-known/openid-configuration"
  "/resources/..;/realms/master/.well-known/openid-configuration"
  "/resources/..%5crealms/master/.well-known/openid-configuration"
  "/resources/%c0%ae%c0%ae/realms/master/.well-known/openid-configuration"
  "/resources/%2e%2e%5crealms/master/.well-known/openid-configuration"
  "/realms/$REALM\\..\\master/.well-known/openid-configuration"
  "/resources/../realms/master/.well-known/openid-configuration"
  "/resources/%2e%2e/realms/master/.well-known/openid-configuration"
  "/resources/..%2frealms/master/.well-known/openid-configuration"
  "/resources/%252e%252e/realms/master/.well-known/openid-configuration"
  "/resources/;/../realms/master/.well-known/openid-configuration"
  "/admin"
  "/admin/"
  "/admin/master/console/"
  "/admin/realms/master"
  "/admin/realms"
  "/%61dmin/master/console/"
  "/realms/$REALM/../../admin/master/console/"
  "/realms/$REALM/%2e%2e/%2e%2e/admin/master/console/"
  "/resources/../admin/master/console/"
  "/js/keycloak.js"
  "/health"
  "/health/ready"
  "/metrics"
  "/"
)
for path in "${CLOSED[@]}"; do
  code=$(public "$path")
  case "$code" in
    400|403|404) leaked=$(reached_master) ;;
    *) leaked="код $code" ;;
  esac
  if [ "$leaked" = нет ]; then ok "закрыто: $path → $code"; else bad "ОТКРЫТО: $path → $code ($leaked)"; fi
done

if [ -n "$DIRECT" ]; then
  echo "2а. Не доходит до Keycloak: соседний realm с общим началом имени и служебные пути"
  # По коду ответа прокси и Keycloak здесь не различить (оба 404): сравниваем с тем, что на этот путь отвечает сам Keycloak.
  # Совпало — запрос до него дошёл, то есть правило шире, чем нужно (например, «/realms/shop*» вместо «/realms/shop/»).
  for path in "/realms/${REALM}x/.well-known/openid-configuration" "/realms/${REALM}-test/.well-known/openid-configuration" \
      "/realms/${REALM}x" "/health" "/metrics" "/js/keycloak.js"; do
    code=$(public "$path"); cp "$WORK/body" "$WORK/public-body"
    direct_code=$(get "$DIRECT" "$path")
    if [ "$code" = "$direct_code" ] && cmp -s "$WORK/body" "$WORK/public-body"; then bad "ДОХОДИТ до Keycloak: $path → $code"
    else ok "не доходит до Keycloak: $path → $code (Keycloak сам: $direct_code)"; fi
  done

  echo "3. Контроль: напрямую (без прокси) те же закрытые пути существуют"
  check "$DIRECT/realms/master/.well-known/openid-configuration" "$(get "$DIRECT" /realms/master/.well-known/openid-configuration)" 200
  check "  это realm master" "$(reached_master)" да
  check "$DIRECT/admin/master/console/" "$(get "$DIRECT" /admin/master/console/)" 200
fi

summary
