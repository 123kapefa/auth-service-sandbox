#!/usr/bin/env bash
# Смена сертификата ключей DataProtection с перекрытием (docs/secrets.md#сертификат-dataprotection). Сами ключи
# DataProtection (ими зашифрованы cookie входа и токены управления ключами сервисов) не меняются — меняется только
# сертификат, которым они зашифрованы в базе, поэтому сессии и токены управления остаются действительными:
#   1. в хранилище: сертификат — новый, прежний — в AUTH_DATA_PROTECTION_PREVIOUS_CERT(_PASSWORD); сервис при старте
#      расшифровывает ключи прежним и перешифровывает новым;
#   2. проверка: в базе не осталось ключей под прежним сертификатом;
#   3. прежний удаляется из хранилища, сервис перезапускается только с новым.
# Запуск (стенд): scripts/secrets/rotate-data-protection-cert.sh
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"

SERVICE="${SERVICE:-auth-service}"
DB_SERVICE="${DB_SERVICE:-postgres}"
DB_USER="${DB_USER:-auth_service}"
DB_NAME="${DB_NAME:-auth_service}"

OLD=$(secret_get AUTH_DATA_PROTECTION_CERT)
OLD_PASSWORD=$(secret_get AUTH_DATA_PROTECTION_CERT_PASSWORD)
[ -n "$OLD" ] || { echo "В хранилище ($SECRETS_ENV) нет AUTH_DATA_PROTECTION_CERT" >&2; exit 1; }
NEW_PASSWORD=$(random_secret)
NEW=$(new_pfx "$NEW_PASSWORD")

# Сертификат (DER в base64) — так он записан в XML ключа (X509Certificate); по нему видно, чем зашифрован ключ.
certificate() { echo "$1" | base64 -d | openssl pkcs12 -nokeys -clcerts -passin env:PASS 2> /dev/null | openssl x509 -outform DER | base64 | tr -d '\n'; }
OLD_DER=$(PASS="$OLD_PASSWORD" certificate "$OLD")
under_old() {
  compose_exec "$DB_SERVICE" psql -U "$DB_USER" -d "$DB_NAME" -tAc \
    "select count(*) from auth.data_protection_keys where xml like '%${OLD_DER:0:120}%'"
}

echo "1. Сервис с новым сертификатом и прежним (ключей под прежним: $(under_old))"
secret_set "AUTH_DATA_PROTECTION_PREVIOUS_CERT=$OLD" "AUTH_DATA_PROTECTION_PREVIOUS_CERT_PASSWORD=$OLD_PASSWORD" \
  "AUTH_DATA_PROTECTION_CERT=$NEW" "AUTH_DATA_PROTECTION_CERT_PASSWORD=$NEW_PASSWORD"
restart "$SERVICE"

echo "2. Ключи перешифрованы новым"
for _ in $(seq 1 30); do [ "$(under_old)" = 0 ] && break; sleep 1; done
LEFT=$(under_old)
[ "$LEFT" = 0 ] || {
  echo "Под прежним сертификатом осталось ключей: $LEFT (журнал сервиса: docker compose logs $SERVICE)." \
    "Прежний оставлен в хранилище — без него эти ключи не расшифровать" >&2
  exit 1
}

echo "3. Прежний сертификат убран"
secret_delete AUTH_DATA_PROTECTION_PREVIOUS_CERT AUTH_DATA_PROTECTION_PREVIOUS_CERT_PASSWORD
restart "$SERVICE"
echo "Готово: сертификат DataProtection сменён, ключи DataProtection прежние"
