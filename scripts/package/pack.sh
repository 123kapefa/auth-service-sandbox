#!/usr/bin/env bash
# NuGet-пакеты сервиса входа для сервисов проекта — в локальный фид artifacts/nuget (версия — Directory.Build.props).
# Подключить в сервисе проекта: nuget.config с этим фидом (deploy/nuget.config.example) и PackageReference
#   Platform.Authentication (+ .Redis — мгновенный отзыв), Platform.AuthService.Client — API сервиса входа по ключу.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${1:-$ROOT/artifacts/nuget}"
mkdir -p "$OUT"
dotnet pack "$ROOT/AuthService.slnx" -c Release -o "$OUT" -v q --nologo > /dev/null
ls -1 "$OUT"/*.nupkg | sed "s#^$ROOT/##"
