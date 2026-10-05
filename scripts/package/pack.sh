#!/usr/bin/env bash
# NuGet-пакеты сервиса входа для сервисов проекта — в локальный фид artifacts/nuget.
# Подключить в сервисе проекта: nuget.config с этим фидом (deploy/nuget.config.example) и PackageReference
#   Platform.Authentication (+ .Redis — мгновенный отзыв), Platform.AuthService.Client — API сервиса входа по ключу.
#
# Версия — VersionPrefix из Directory.Build.props. Для локального фида — с суффиксом сборки (0.1.0-dev.20261005120000):
# у каждой сборки своя версия, и проект не берёт из кэша NuGet старый пакет с той же версией. Рядом — props с этой
# версией (platform-version.props): проект на локальном фиде импортирует его (как samples/shop/Directory.Packages.props).
# Старые пакеты Platform.* из фида удаляются. RELEASE=1 — версия без суффикса (в фид компании).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${1:-$ROOT/artifacts/nuget}"
PREFIX=$(sed -n 's#.*<VersionPrefix>\(.*\)</VersionPrefix>.*#\1#p' "$ROOT/Directory.Build.props" | head -1)
if [ -n "${RELEASE:-}" ]; then
  VERSION="$PREFIX"
else
  VERSION="$PREFIX-dev.$(date -u +%Y%m%d%H%M%S)"
fi
mkdir -p "$OUT"
rm -f "$OUT"/Platform.*.nupkg
dotnet pack "$ROOT/AuthService.slnx" -c Release -o "$OUT" -p:Version="$VERSION" -v q --nologo > /dev/null
cat > "$OUT/platform-version.props" <<PROPS
<Project>
  <!-- Версия пакетов Platform.* в этом фиде (scripts/package/pack.sh). -->
  <PropertyGroup>
    <PlatformVersion>$VERSION</PlatformVersion>
  </PropertyGroup>
</Project>
PROPS
ls -1 "$OUT"/*.nupkg | sed "s#^$ROOT/##"
