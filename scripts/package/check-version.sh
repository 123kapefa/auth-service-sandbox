#!/usr/bin/env bash
# Одна версия у всего, что выпускает релиз: VersionPrefix в Directory.Build.props — пакеты и образы; version и
# appVersion чарта (тег образов в Kubernetes по умолчанию); AUTH_VERSION в deploy/.env.example. С тегом
# (scripts/package/check-version.sh v0.2.0) — и он. Печатает версию; расхождение — ошибка с перечнем мест.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CHART="$ROOT/deploy/helm/platform-auth/Chart.yaml"
version=$(sed -n 's#.*<VersionPrefix>\(.*\)</VersionPrefix>.*#\1#p' "$ROOT/Directory.Build.props" | head -1)

declare -A found=(
  ["deploy/helm/platform-auth/Chart.yaml: version"]=$(sed -n 's/^version:[[:space:]]*//p' "$CHART" | tr -d '"')
  ["deploy/helm/platform-auth/Chart.yaml: appVersion"]=$(sed -n 's/^appVersion:[[:space:]]*//p' "$CHART" | tr -d '"')
  ["deploy/.env.example: AUTH_VERSION"]=$(sed -n 's/^AUTH_VERSION=//p' "$ROOT/deploy/.env.example")
)
[ -n "${1:-}" ] && found["тег $1"]="${1#v}"

failed=0
for place in "${!found[@]}"; do
  if [ "${found[$place]}" != "$version" ]; then
    echo "::error::$place — ${found[$place]:-нет}, а VersionPrefix в Directory.Build.props — $version" >&2
    failed=1
  fi
done
[ "$failed" = 0 ] || exit 1
echo "$version"
