# Auth Service (песочница)

Переиспользуемый сервис авторизации на .NET 10 поверх **Keycloak**.
Keycloak отвечает за вход (пароль, почта, телефон, внешние провайдеры, 2FA, passkey)
и хранит пользователей и роли; этот сервис — обёртка над ним: проверка токенов,
роли и режимы, операции админки через Admin API Keycloak, свои данные в PostgreSQL.

> Исходная реализация на OpenIddict сохранена тегом `openiddict-baseline`.
> Документация: [docs/README.md](docs/README.md). Для разработчиков сервиса — [техническая документация](docs/tech/README.md) и [решения (ADR)](docs/tech/adr/README.md).

## Состояние

Сервис ведёт вход через страницу Keycloak (code + PKCE), держит сессию в cookie `accessToken`/`refreshToken` с
обновлением и выходом, проверяет токены и пускает к эндпоинтам по ролям, режимам и правам приложений. Способы входа:
телефон или почта с паролем, коды (звонок, SMS, письмо), сброс пароля, 2FA, ключи доступа, Яндекс ID и Госуслуги.
Есть своя админка, личный кабинет и оформление под проект. Демо-страница — http://localhost:8080. Сервис собирается
и проверяется в CI, выпускается тегом и ставится через Docker Compose или в Kubernetes.

Что умеет и как пользоваться — [docs/overview.md](docs/overview.md), запуск — [docs/getting-started.md](docs/getting-started.md).

Встроить сервис входа в свой проект — [руководство «Как встроить»](docs/embedding-guide.md).

## Структура

| Проект | Слой |
|---|---|
| `AuthService.Api` | web-слой: Program, эндпоинты, конфигурация |
| `AuthService.Core` | сценарии (use cases) |
| `AuthService.Domain` | доменная модель |
| `AuthService.Contracts` | запросы/ответы и клиент для других сервисов |
| `AuthService.Infrastructure.Postgres` | EF Core, схема `auth` |

## Локальный запуск

```bash
cp .env.example .env
```

```bash
docker compose up -d --wait
```

| Что | Адрес |
|---|---|
| Auth Service | http://localhost:8080/health/ready |
| Keycloak (админка) | http://localhost:8180/admin |
| Mailpit | http://localhost:8025 |

Окружения `Development` (`dotnet run`, порт 5080), `Docker` (compose, порт 8080) и `Production`,
и что где хранится — [docs/environments.md](docs/environments.md). Секреты — в хранилище Infisical, стенд с ними —
`scripts/secrets/run.sh docker compose up -d --wait`; перечень, проверка утечек и смена — [docs/secrets.md](docs/secrets.md).

Новая версия на работающем стенде или в бою: миграции базы и настроек проектов, резервные копии, обновление Keycloak —
[docs/migrations.md](docs/migrations.md). Запуск в Kubernetes (Helm-чарт, Keycloak в несколько реплик, стенд в kind) —
[docs/kubernetes.md](docs/kubernetes.md).

## Выпуск и развёртывание

Версия выпускается тегом `vX.Y.Z`: workflow «Release» кладёт образы `platform-auth-service`, `platform-auth-keycloak` и
`platform-auth-realm-migrator` в GHCR (`ghcr.io/123kapefa/…`), пакеты `Platform.*` в GitHub Packages и создаёт релиз
GitHub. Версия одна на пакеты, образы и чарт (`scripts/package/check-version.sh`). Проект ставит релиз через Docker
Compose ([как встроить](docs/embedding-guide.md)) или в Kubernetes Helm-чартом, секреты берутся из Infisical.

Пошагово, от номера версии до установки в кластер, — [docs/release.md](docs/release.md). Как устроены CI, релиз,
секреты, миграции и Kubernetes — [архитектура](docs/tech/architecture.md#инфраструктура).

## CI

На каждый PR и push в `main`: **CI** (`.github/workflows/ci.yml`) — сборка .NET с анализаторами, модель EF против
миграций, тесты (быстрые и на Testcontainers), админка и тема входа (lint, типы, Vitest, сборка), плагин Keycloak,
уязвимые пакеты; **secrets** — gitleaks по всей истории. Долгие проверки — стенд и Kubernetes — идут ночью, на тег и
на PR, который их касается (ниже и в [kubernetes.md](docs/kubernetes.md#стенд-в-kind)).

## Тесты

```bash
dotnet test                                        # все: нужен только Docker, docker compose up не нужен
dotnet test --filter "Category!=Infrastructure"    # быстрые, без Docker
```

`AuthService.InfrastructureTests` сами поднимают Postgres, Redis и Keycloak (наш образ из `infra/keycloak/Dockerfile`
с realm из `infra/keycloak/realm`) через Testcontainers. Образ Keycloak тесты собирают сами (`docker build`, повторно — из
кэша слоёв); готовый можно дать переменной `KEYCLOAK_TEST_IMAGE`. Остальные тесты работают на подделках.

## Живые проверки стенда

Скрипты `scripts/keycloak/check-*.sh` проверяют поднятый стенд по-настоящему: вход через страницу Keycloak, коды,
админку, кабинет, магазин на сервисе входа, перенос пользователей. В CI их гоняет workflow «Стенд»
(`.github/workflows/stand.yml`) ночью, на тег `v*`, вручную (Actions → Стенд → Run workflow) и на PR, который меняет
сами проверки (`scripts/keycloak/`) или workflow:

- **Стенд песочницы:** `docker compose up -d --build --wait`, разрешение на регистрацию клиентов
  (`registration-token.sh --write-env`), затем `check-stand`, `check-api`, `check-login`, `check-admin`, `check-6b`,
  `check-10`, `check-shop`, `check-migration` — каждый отдельным шагом.
- **Развёртывание из образов:** `scripts/package/build-images.sh` и `check-deploy.sh` по `deploy/compose.yaml`.

Упала проверка — в артефакте запуска (`stand-logs` или `deploy-logs`) вывод каждого скрипта и логи всех контейнеров
(`scripts/ci/collect-logs.sh`).
