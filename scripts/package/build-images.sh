#!/usr/bin/env bash
# Образы сервиса входа с версией (Directory.Build.props — та же, что у NuGet-пакетов):
#   platform-auth-service:<версия>   — сервис (API, админка /admin) и мигратор его базы (./efbundle)
#   platform-auth-keycloak:<версия>  — Keycloak + плагины (вход по коду, BCrypt, роли приложения) + тема platform
#   platform-auth-realm-migrator:<версия> — мигратор настроек проектов (realm), docs/migrations.md
# Плюс тег latest. Отправить в реестр компании: REGISTRY=registry.example.com/platform scripts/package/build-images.sh --push
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION=$(sed -n 's#.*<VersionPrefix>\(.*\)</VersionPrefix>.*#\1#p' "$ROOT/Directory.Build.props" | head -1)
PREFIX="${REGISTRY:+$REGISTRY/}"

build() {
  docker build -q -t "${PREFIX}$1:$VERSION" -t "${PREFIX}$1:latest" "${@:2}" "$ROOT" > /dev/null
  echo "${PREFIX}$1:$VERSION"
}

build platform-auth-service --target api -f "$ROOT/Dockerfile"
build platform-auth-keycloak -f "$ROOT/infra/keycloak/Dockerfile"
build platform-auth-realm-migrator -f "$ROOT/infra/realm-migrator/Dockerfile"

if [ "${1:-}" = "--push" ]; then
  for image in platform-auth-service platform-auth-keycloak platform-auth-realm-migrator; do
    # Отдельными командами: при set -e сбой левой части «a && b» скрипт не останавливает.
    docker push -q "${PREFIX}$image:$VERSION"
    docker push -q "${PREFIX}$image:latest"
  done
fi
