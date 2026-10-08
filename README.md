# Auth Service (песочница)

Переиспользуемый сервис авторизации на .NET 10 поверх **Keycloak**.
Keycloak отвечает за вход (пароль, почта, телефон, внешние провайдеры, 2FA, passkey)
и хранит пользователей и роли; этот сервис — обёртка над ним: проверка токенов,
роли и режимы, операции админки через Admin API Keycloak, свои данные в PostgreSQL.

> Исходная реализация на OpenIddict сохранена тегом `openiddict-baseline`.
> Цели и этапы: [docs/keycloak/README.md](docs/keycloak/README.md). Итог — [выводы](docs/keycloak/08-conclusions.md) и [решения (ADR)](docs/adr/README.md).

## Состояние

Идёт перестройка по архитектурной документации платформы (Clean Architecture,
4 слоя + Api). Сейчас сервис ведёт вход через страницу Keycloak (code + PKCE), держит сессию в cookie
`accessToken`/`refreshToken` с обновлением и выходом (этап 3), проверяет токены и пускает к эндпоинтам
по ролям, активному режиму и правам (этап 2). Способы входа (этап 3а): телефон или почта с паролем,
сброс пароля по почте, 2FA по желанию, ключ доступа (passkey). Демо-страница для проверки — http://localhost:8080.

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
и что где хранится — [docs/environments.md](docs/environments.md).

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
