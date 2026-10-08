#!/usr/bin/env bash
# Модель EF базы сервиса не разошлась с миграциями (docs/migrations.md): правка сущностей без миграции — ошибка.
# Нужны .NET SDK и dotnet-ef той же версии, что пакеты EF Core:
#   dotnet tool install --global dotnet-ef --version 10.0.7
# Запуск: scripts/db/check-model.sh; в CI после сборки — scripts/db/check-model.sh --configuration Release --no-build
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$ROOT/src/AuthService.Infrastructure.Postgres"
if ! dotnet ef migrations has-pending-model-changes --project "$PROJECT" --startup-project "$PROJECT" "$@"; then
  echo "Модель изменилась без миграции: dotnet ef migrations add <Название> --project $PROJECT --startup-project $PROJECT" >&2
  exit 1
fi
