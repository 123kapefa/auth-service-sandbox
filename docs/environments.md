# Окружения: Development, Docker, Production

Код один, отличаются только значения настроек. Окружение выбирает переменная `ASPNETCORE_ENVIRONMENT`.

| Окружение | Где работает сервис | Адрес | Кто задаёт окружение |
|---|---|---|---|
| `Development` | на машине, `dotnet run`; база — в Docker | http://localhost:5080 | `Properties/launchSettings.json` |
| `Docker` | контейнер `auth-service` в compose | http://localhost:8080 | `compose.yaml` |
| `Production` | прод-развёртывание (в песочнице — только проверка запуска) | домен `auth-sandbox.example`, https на прокси | окружение развёртывания |

## Как .NET собирает настройки

Слои, каждый следующий перекрывает предыдущий:

1. `appsettings.json` — общая база для всех окружений.
2. `appsettings.{Окружение}.json` — только текущего окружения. Остальные файлы лежат в образе, но не читаются.
3. Переменные окружения: `ConnectionStrings__AuthDb=...` перекрывает `ConnectionStrings:AuthDb`
   (двойное подчёркивание = двоеточие, элемент массива — `__0`, `__1`).
4. Аргументы командной строки.

Ловушка: `dotnet run` без профиля запуска стартует как **Production**. Профиль с `Development` лежит в
`src/AuthService.Api/Properties/launchSettings.json` (в образ не попадает).

## Что куда класть

| Что | Куда | Пример |
|---|---|---|
| Несекретное и общее для всех | `appsettings.json` | уровни логов по умолчанию |
| Несекретное, своё у окружения | `appsettings.{Окружение}.json` | `AllowedHosts` прода, подробные логи в Development |
| Секреты | **только** переменные окружения (в проде — из хранилища секретов) | `ConnectionStrings__AuthDb` (внутри пароль) |
| Разное у разных копий одного окружения | переменные окружения | домен и база конкретного прод-контура |

Исключение — **Development**: dev-плейсхолдеры (`development-only`) допустимы прямо в файле, как и в `.env.example`.
В файл попадает только то, что читает код: настройка без кода — мёртвая строка, которая вводит в заблуждение.

## Настройки и их значения

| Настройка | Кто читает | Development | Docker | Production |
|---|---|---|---|---|
| `ConnectionStrings:AuthDb` | Infrastructure; без неё сервис не стартует | в файле, `localhost:5432` | из `compose.yaml` (`.env`), `postgres:5432` | переменная окружения |
| `AllowedHosts` | ASP.NET Core; чужой `Host` → 400 | `*` (база) | `*` (база) | `auth-sandbox.example;localhost` |
| `ForwardedHeaders:TrustedNetworks` | `ForwardedHeadersConfiguration` | по умолчанию в коде: `172.16.0.0/12`, `127.0.0.0/8` | так же | так же; задать, если прокси в другой сети |
| `Logging:LogLevel` | ASP.NET Core | Debug | Debug для `AuthService`, фреймворк — Warning | Warning |

## Почему у Development и Docker разные адреса

Внутри контейнера `localhost` — это сам контейнер. Поэтому:

- **Development**: сервис на машине ходит в Postgres через проброшенный порт `localhost:${POSTGRES_PORT:-5432}`;
- **Docker**: сервис в контейнере ходит к соседям по имени сервиса compose — `postgres:5432`.

На этапе 2 то же самое будет с Keycloak: браузер и `iss` в токенах — `http://localhost:8180`, а контейнер
скачивает ключи с `http://keycloak:8180`. Блок `Authentication` появится во всех трёх файлах вместе с кодом,
который его читает.

## Запуск

**Development** — база в Docker, сервис на машине (удобно для отладки):

```bash
docker compose up -d --wait postgres
```

```bash
dotnet run --project src/AuthService.Api
```

Если порт 5432 занят, поменяйте `POSTGRES_PORT` в `.env` **и** порт в `appsettings.Development.json`.

**Docker** — всё в контейнерах:

```bash
docker compose up -d --build --wait auth-service
```

**Production** — в файле нет ни одного секрета. Без строки подключения сервис не стартует:
`ConnectionStrings:AuthDb is required.`

Проверка: `curl http://localhost:<порт>/health/ready` → `Healthy`.

## Грабли

- **dotnet как PID 1.** Упавший при старте процесс в контейнере не завершается (ядро игнорирует `SIGABRT`
  для PID 1), контейнер висит в `running`. В `compose.yaml` стоит `init: true`; в проде нужно то же —
  `--init`, `tini` или аналог оркестратора.
- **`AllowedHosts` в проде** — проверки здоровья от оркестратора по IP пода получат 400. Для них оставлен
  `localhost`; если проверка ходит по другому имени — добавьте его.
- **Таблицы в схеме `auth`**, а не `public`: `select * from auth.<таблица>`.
