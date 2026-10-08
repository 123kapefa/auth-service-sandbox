# Окружения: Development, Docker, Production

Код один, отличаются только значения настроек. Окружение выбирает переменная `ASPNETCORE_ENVIRONMENT`.

| Окружение | Где работает сервис | Адрес | Кто задаёт окружение |
|---|---|---|---|
| `Development` | на машине, `dotnet run`; база — в Docker | http://localhost:5080 | `Properties/launchSettings.json` |
| `Docker` | контейнер `auth-service` в compose | http://localhost:8080 | `compose.yaml` |
| `Production` | прод: `deploy/compose.yaml` на одной машине или Helm-чарт в Kubernetes (в песочнице — стенд из образов и kind) | домен проекта, https на прокси или Ingress | `deploy/.env` или values чарта |

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
| Секреты | **только** переменные окружения из хранилища секретов ([secrets.md](secrets.md)) | `ConnectionStrings__AuthDb` (внутри пароль) |
| Разное у разных копий одного окружения | переменные окружения | домен и база конкретного прод-контура |

Исключение — **Development**: dev-плейсхолдеры (`development-only`) допустимы прямо в файле, как и в `.env.example`.
В файл попадает только то, что читает код: настройка без кода — мёртвая строка, которая вводит в заблуждение.

## Настройки и их значения

| Настройка | Кто читает | Development | Docker | Production |
|---|---|---|---|---|
| `ConnectionStrings:AuthDb` | Infrastructure; без неё сервис не стартует | в файле, `localhost:5432` | из `compose.yaml` (`.env`), `postgres:5432` | переменная окружения |
| `ConnectionStrings:Redis` | мгновенный отзыв токенов (`Shared/Authentication.Redis`); без неё сервис не стартует | в файле, `localhost:6379` | из `compose.yaml`, `redis:6379` | переменная окружения |
| `TokenRevocation:TokenLifetime` / `LocalCache` | сколько хранить отметку / кэш проверки в памяти | по умолчанию в коде: 16 мин / 2 с | так же | так же; `LocalCache` держать короткой |
| `AllowedHosts` | ASP.NET Core; чужой `Host` → 400 | `*` (база) | `*` (база) | `auth-sandbox.example;localhost` |
| `ForwardedHeaders:TrustedNetworks` | `ForwardedHeadersConfiguration` | по умолчанию в коде: `172.16.0.0/12`, `127.0.0.0/8` | так же | так же; задать, если прокси в другой сети |
| `Authentication:Authority` | проверка токенов (`iss`) | `http://localhost:8180/realms/sandbox` | так же | `https://id.auth-sandbox.example/realms/sandbox` |
| `Authentication:MetadataAddress` | откуда брать ключи | — (из Authority) | `http://keycloak:8180/.../openid-configuration` | задать, если сервер входа доступен по внутреннему адресу |
| `Authentication:Audience` | проверка токенов (`aud`) | `sandbox-api` (база) | так же | так же |
| `Authentication:RequireHttpsMetadata` | https для discovery | `false` | `false` | `true` (по умолчанию) |
| `Keycloak:ClientId` / `ClientSecret` | клиент входа `sandbox-bff` | `sandbox-bff` / dev-заглушка в файле | так же / из хранилища (или `.env`) через compose | так же / только переменная `Keycloak__ClientSecret` |
| `Keycloak:PreviousClientSecret` | прежний секрет — только на время смены ([secrets.md](secrets.md#секрет-bff)) | — | так же | так же |
| `DataProtection:Certificate` / `CertificatePath`, `CertificatePassword` | сертификат ключей DataProtection: PFX в base64 или файл | — (ключи не шифруются) | из хранилища; без него ключи не шифруются | обязателен (`RequireCertificate`) |
| `DataProtection:PreviousCertificate*` | прежний сертификат — только на время смены ([secrets.md](secrets.md#сертификат-dataprotection)) | — | так же | так же |
| `Cookies:Secure` / `Domain` | cookie входа | `false` / — | `false` / — | `true` / `.auth-sandbox.example` |
| `ReturnUrls:AllowedOrigins` | куда вернуть после входа | `localhost:5080/3000/5173` | `localhost:8080/3000/5173` | приложения платформы |
| `Logging:LogLevel` | ASP.NET Core | Debug | Debug для `AuthService`, фреймворк — Warning | Warning |

## Почему у Development и Docker разные адреса

Внутри контейнера `localhost` — это сам контейнер. Поэтому:

- **Development**: сервис на машине ходит в Postgres через проброшенный порт `localhost:${POSTGRES_PORT:-5432}`;
- **Docker**: сервис в контейнере ходит к соседям по имени сервиса compose — `postgres:5432`.

То же с Keycloak: браузер и `iss` в токенах — `http://localhost:8180` (`Authentication:Authority`), а контейнер
скачивает ключи с `http://keycloak:8180` (`Authentication:MetadataAddress`). Подробно —
[keycloak/02-token-api.md](keycloak/02-token-api.md).

## Запуск

**Development** — база, Redis и миграции в Docker, сервис на машине (удобно для отладки):

```bash
docker compose up -d --wait postgres redis
```

```bash
docker compose up auth-migrator
```

```bash
dotnet run --project src/AuthService.Api
```

Если порт 5432 занят, поменяйте `POSTGRES_PORT` в `.env` **и** порт в `appsettings.Development.json`.

**Docker** — всё в контейнерах:

```bash
docker compose up -d --build --wait auth-service
```

Мигратор (`efbundle` из образа сервиса) применяет миграции и завершается; в Docker сервис ждёт его успеха сам — и успеха
мигратора настроек проектов (`realm-migrator`, после Keycloak). Подробно, с резервными копиями — [migrations.md](migrations.md).
Back-channel logout Keycloak в Development не работает: Keycloak из контейнера зовёт `auth-service:8080`.

**Production** — образы релиза из реестра, не сборка ([release.md](release.md)). В файле нет ни одного секрета. Без строк подключения сервис не стартует:
`ConnectionStrings:AuthDb is required.`, `ConnectionStrings:Redis is required.` Миграции — тем же `efbundle`
отдельным шагом развёртывания до старта сервиса, перед ним — резервная копия базы ([migrations.md](migrations.md)).

Проверка: `curl http://localhost:<порт>/health/ready` → `Healthy`.

## Production в Kubernetes

Чарт `deploy/helm/platform-auth` задаёт настройки сервиса переменными окружения из values, секреты — из Secret,
который пишет оператор Infisical ([kubernetes.md](kubernetes.md#секреты)). `appsettings.Production.json` из образа
читается, но всё своё у проекта приходит из values:

| Настройка | Откуда в Kubernetes |
|---|---|
| `ConnectionStrings:AuthDb` | `database.*` и `AUTH_DB_PASSWORD` из Secret |
| `ConnectionStrings:Redis` | `redis` |
| `Authentication:Authority` | `publicUrls.keycloak` + `realm` |
| `Authentication:MetadataAddress` | внутренний адрес Keycloak в кластере (`http://<релиз>-keycloak:8180`), сам |
| `Authentication:Audience`, `Keycloak:ClientId` | `service.audience`, `service.clientId` (`auth-api`, `auth-bff` — как у `create-project.sh`) |
| `Keycloak:ClientSecret`, `PreviousClientSecret`, `RegistrationToken` | `AUTH_BFF_CLIENT_SECRET`, `AUTH_BFF_CLIENT_SECRET_PREVIOUS`, `AUTH_REGISTRATION_TOKEN` из Secret |
| `DataProtection:CertificatePath` и пароль | файл из Secret `<релиз>-data-protection`, пароль — `AUTH_DATA_PROTECTION_CERT_PASSWORD` |
| `AllowedHosts` | `service.allowedHosts`, по умолчанию домен `publicUrls.service` и `localhost` (пробы kubelet) |
| `ForwardedHeaders:TrustedNetworks` | `service.trustedNetworks` — сеть подов, по умолчанию `10.0.0.0/8` |
| `Cookies:Secure` / `Domain` | `service.cookies.*` |
| `ReturnUrls:AllowedOrigins` | `service.returnOrigins` |
| остальное | `service.extraEnv` |

Пропущено обязательное значение — `helm template` и `helm install` падают с подсказкой, что задать.

## Грабли

- **dotnet как PID 1.** Упавший при старте процесс в контейнере не завершается (ядро игнорирует `SIGABRT`
  для PID 1), контейнер висит в `running`. В `compose.yaml` стоит `init: true`; в проде нужно то же —
  `--init`, `tini` или аналог оркестратора (в чарте — общее пространство процессов пода, PID 1 у него `pause`).
- **`AllowedHosts` в проде** — проверки здоровья от оркестратора по IP пода получат 400. Для них оставлен
  `localhost`; если проверка ходит по другому имени — добавьте его.
- **Таблицы в схеме `auth`**, а не `public`: `select * from auth.<таблица>`.
