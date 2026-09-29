# Auth Service (песочница)

Переиспользуемый сервис авторизации на .NET 10 поверх **Keycloak**.
Keycloak отвечает за вход (пароль, почта, телефон, внешние провайдеры, 2FA, passkey)
и хранит пользователей и роли; этот сервис — обёртка над ним: проверка токенов,
роли и режимы, операции админки через Admin API Keycloak, свои данные в PostgreSQL.

> Исходная реализация на OpenIddict сохранена тегом `openiddict-baseline`.
> Цели и этапы: [docs/keycloak/README.md](docs/keycloak/README.md).

## Состояние

Идёт перестройка по архитектурной документации платформы (Clean Architecture,
4 слоя + Api). Сейчас сервис ведёт вход через страницу Keycloak (code + PKCE), держит сессию в cookie
`accessToken`/`refreshToken` с обновлением и выходом (этап 3), проверяет токены и пускает к эндпоинтам
по ролям, активному режиму и правам (этап 2). Способы входа (этап 3а): телефон или почта с паролем,
сброс пароля по почте, 2FA по желанию, ключ доступа (passkey). Демо-страница для проверки — http://localhost:8080.

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
