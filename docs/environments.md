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
| `ConnectionStrings:Redis` | мгновенный отзыв токенов (`Shared/Authentication.Redis`); `host:port[,user=…,password=…]` ([ACL](#redis-пользователи)); без неё сервис не стартует | в файле, `localhost:6379` | из `compose.yaml`, `redis:6379` | переменная окружения |
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
| `DemoPage:Enabled` | демо-страница разработчика `/dev/` | `true` | `true` | нет: `/dev/` — 404, включить нельзя (старт остановится) |
| `Metrics:Port` | порт метрик Prometheus (`/metrics`, наружу не публикуется); `0` — выключено ([monitoring.md](monitoring.md)) | `0` | `9100` (на хосте — `127.0.0.1`) | `9100` (по умолчанию) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | адрес OTLP-коллектора для трасс; пусто — spans никуда не уходят ([monitoring.md](monitoring.md#логи-и-сквозная-трассировка)) | — | `AUTH_OTEL_ENDPOINT` (пусто) | `AUTH_OTEL_ENDPOINT` / `tracing.otlpEndpoint` |
| `Logging:Console` | формат журнала: JSON (в Docker и Production), текст (Development) | текст | JSON | JSON |
| `Cookies:Secure` | cookie входа (только для хоста сервиса входа, `Domain` нет); `true` — имена `__Host-accessToken`/`__Host-refreshToken` | `false` | `false` | `true` |
| `ReturnUrls:AllowedOrigins` | куда вернуть после входа | `localhost:5080/3000/5173` | `localhost:8080/3000/5173` | приложения платформы |
| `Logging:LogLevel` | ASP.NET Core | Debug | Debug для `AuthService`, фреймворк — Warning | Warning |
| `TokenRevocation:OnStoreFailure` | что делать, когда Redis с отметками отзыва недоступен: `Open` / `Closed` | `Open` | `Open` | `Open` по умолчанию; `Closed` — см. «Сбои Redis и капчи» |
| `KC_SPI_…_CAPTCHA_ON_OUTAGE` (Keycloak) | что делать, когда SmartCaptcha недоступна: `pass` / `reject` | `pass` | `pass` | `pass` по умолчанию; `reject` — см. ниже |
| `KC_SPI_…_ALLOW_STUBS` (Keycloak) | разрешить заглушки стенда (`CAPTCHA_STUB`, `PHONE_STUB`) вне `start-dev` | — (в `start-dev` не нужен) | — | нет: заглушка в бою останавливает старт Keycloak |

## Сбои Redis и капчи: открыто или закрыто

Две защиты зависят от внешней системы. Когда она недоступна, нужно выбрать: **не мешать входу** (открыто) или **не терять защиту**
(закрыто). Оба режима явные настройки, по умолчанию — «открыто», как было.

| Что недоступно | Открыто (по умолчанию) | Закрыто |
|---|---|---|
| **Redis с отметками отзыва** (`TokenRevocation:OnStoreFailure`) | токен пропускается (он живёт не дольше 15 минут), в лог — предупреждение. Вход и API работают, но **отзыв на это время не действует**: заблокированный или разлогиненный человек ещё входит | `Closed`: токен не принимается, ответ **503** с `Retry-After: 5` и кодом `auth.revocation_unavailable`. Отзыв не обойти, но недоступность Redis кладёт и API |
| **Яндекс SmartCaptcha** (`CAPTCHA_ON_OUTAGE`) | `pass`: капча считается пройденной, лимиты кодов действуют (так советует Яндекс) | `reject`: капча не пройдена, пока сервис недоступен; там, где лимиты требуют капчу, код не отправляется |

Как выбрать: если важнее непрерывность входа — «открыто» (токены и так живут 15 минут, а лимиты остаются). Если отзыв — требование безопасности
(например, блокировка должна действовать немедленно всегда) — «закрыто», но держите Redis отказоустойчивым (реплики, управляемая база).
Ошибка в значении (`Clsed`, `closed` для капчи) останавливает старт, а не молча остаётся «открыто». Метрика отказов капчи — `platform_code_captcha_unavailable_total`.

**Заглушки стенда** (`CAPTCHA_STUB`: капча «галочкой»; `PHONE_STUB`: коды в лог) работают только в `start-dev` или с явным `ALLOW_STUBS=true`
(kind и закрытый стенд). Перенесённый в бой флаг без разрешения останавливает старт Keycloak с понятным сообщением.

## Redis: пользователи

В Redis лежат отметки отзыва токенов (ключи `auth:revoked:*` и `auth:changed:*`). Писать их должен только сервис входа; сервисам проекта
(пакет `Platform.Authentication.Redis`) нужно только читать. Если Redis общий и без пользователей, любой сервис проекта может стереть отметки
(`DEL`, `FLUSHALL`) или выставить фальшивые — и мгновенный отзыв перестанет работать. Поэтому:

- пример в [`deploy/redis/redis.conf.example`](../deploy/redis/redis.conf.example): `default` выключен, `auth-service` читает и пишет только эти ключи,
  `project-readonly` только читает (`MGET`, `GET`, `PING` и то, что клиент делает при подключении). Пароли замените и держите в хранилище секретов;
- сервис входа: `ConnectionStrings__Redis=host:6379,user=auth-service,password=…` (в Helm — `redisFromSecret: true` и ключ `AUTH_REDIS` в Secret);
- сервисы проекта: то же с пользователем `project-readonly`;
- пример проверяется тестом на настоящем Redis (`RedisAclTests`): запись, `DEL`, `FLUSHALL`, `KEYS` и чужие ключи у `project-readonly` — отказ.

## Прокси к Keycloak: что пускать наружу

Снаружи Keycloak нужен только realm проекта и ресурсы тем. Realm master (`/realms/master`, вход мастер-админа) и `/admin`
(мастер-админка) наружу не отдаются: мастер-админ — это все проекты сразу, и пароль, открытый в интернет, можно перебирать.
Прокси пускает **только** `/realms/<код проекта>` и `/resources`, остальное отвечает 404, не доходя до Keycloak. Готовые
примеры — [nginx](../deploy/proxy/nginx.conf.example) и [Caddy](../deploy/proxy/Caddyfile.example): замените
`id.shop.example.com` и `shop` (код проекта). Правило писать от разрешённого, а не от запрещённого, и без `*` на конце
(`/realms/shop*` открыл бы `/realms/shop-test`).

К мастер-админке ходят с машины развёртывания: напрямую на `127.0.0.1:<AUTH_KEYCLOAK_PORT>` или через SSH-туннель
(в Kubernetes — `kubectl port-forward`). Свой прокси (Traefik, HAProxy, облачный балансировщик) проверьте матрицей обходов:

```bash
PUBLIC=https://id.shop.example.com REALM=shop scripts/keycloak/check-public-paths.sh
```

Скрипт шлёт около сорока путей как есть (`..`, `%2e`, `%2f`, двойное кодирование, `//`, регистр, `;`, `/admin`) и требует
4xx без признаков master; для контроля `DIRECT=http://127.0.0.1:8180` покажет, что напрямую те же пути отвечают. Примеры
nginx и Caddy и Ingress чарта проходят её в CI на каждый релиз. Встроенную защиту Keycloak от перебора пароля у realm
master мы не включаем: пока она включена, параллельные входы мастер-админа (скрипты, мигратор) отклоняются как
«пользователь временно заблокирован». Ограничение частоты запросов к `/realms/<код>` — на прокси: в примере nginx
оно есть (`limit_req`, 10 запросов в секунду с адреса, 429 сверх), у Caddy без плагина такого нет — ставьте лимит перед ним; решение и что отвергли — [ADR 0014](tech/adr/0014-master-realm-closed-at-proxy.md).

## Почему у Development и Docker разные адреса

Внутри контейнера `localhost` — это сам контейнер. Поэтому:

- **Development**: сервис на машине ходит в Postgres через проброшенный порт `localhost:${POSTGRES_PORT:-5432}`;
- **Docker**: сервис в контейнере ходит к соседям по имени сервиса compose — `postgres:5432`.

То же с Keycloak: браузер и `iss` в токенах — `http://localhost:8180` (`Authentication:Authority`), а контейнер
скачивает ключи с `http://keycloak:8180` (`Authentication:MetadataAddress`). Подробно —
[tech/pitfalls.md](tech/pitfalls.md#realm-и-keycloak).

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
| `Cookies:Secure` | `service.cookies.secure` (`Domain` нет: cookie только для хоста сервиса входа) |
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
