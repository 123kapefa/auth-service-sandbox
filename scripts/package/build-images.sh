#!/usr/bin/env bash
# Образы сервиса входа с версией (Directory.Build.props — та же, что у NuGet-пакетов):
#   platform-auth-service:<версия>   — сервис (API, админка /admin) и мигратор его базы (./efbundle)
#   platform-auth-keycloak:<версия>  — Keycloak + плагины (вход по коду, BCrypt, роли приложения) + тема platform
# Плюс тег latest. Отправить в реестр компании: REGISTRY=registry.example.com/platform scripts/package/build-images.sh --push
# Релиз по тегу (.github/workflows/release.yml) отправляет их в GHCR: REGISTRY=ghcr.io/123kapefa.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION=$(sed -n 's#.*<VersionPrefix>\(.*\)</VersionPrefix>.*#\1#p' "$ROOT/Directory.Build.props" | head -1)
PREFIX="${REGISTRY:+$REGISTRY/}"

build() {
  # Метка source связывает образ в GHCR с репозиторием (страница пакета, права и видимость — от репозитория).
  docker build -q -t "${PREFIX}$1:$VERSION" -t "${PREFIX}$1:latest" \
    --label "org.opencontainers.image.source=https://github.com/123kapefa/auth-service-sandbox" \
    --label "org.opencontainers.image.version=$VERSION" \
    "${@:2}" "$ROOT" > /dev/null
  echo "${PREFIX}$1:$VERSION"
}

build platform-auth-service --target api -f "$ROOT/Dockerfile"
build platform-auth-keycloak -f "$ROOT/infra/keycloak/Dockerfile"

if [ "${1:-}" = "--push" ]; then
  for image in platform-auth-service platform-auth-keycloak; do
    # Отдельными командами: при set -e сбой левой части «a && b» скрипт не останавливает.
    docker push -q "${PREFIX}$image:$VERSION"
    docker push -q "${PREFIX}$image:latest"
  done
fi
