#!/usr/bin/env bash
# Новый сертификат для ключей DataProtection (RSA 2048, 10 лет): печатает PFX в base64 одной строкой — значение
# AUTH_DATA_PROTECTION_CERT в хранилище секретов. Пароль PFX — переменная PFX_PASSWORD (не аргумент: аргументы видны
# в списке процессов). Закрытый ключ на диск не пишется: openssl читает и пишет через каналы.
#   PFX_PASSWORD=... scripts/secrets/new-pfx.sh
set -euo pipefail

[ -n "${PFX_PASSWORD:-}" ] || { echo "Нужен PFX_PASSWORD" >&2; exit 2; }
KEY=$(openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 2> /dev/null)
openssl pkcs12 -export -passout env:PFX_PASSWORD -keypbe AES-256-CBC -certpbe AES-256-CBC -macalg sha256 \
  -inkey <(echo "$KEY") \
  -in <(openssl req -x509 -new -key <(echo "$KEY") -days 3650 -subj /CN=auth-data-protection 2> /dev/null) |
  base64 | tr -d '\n'
echo
