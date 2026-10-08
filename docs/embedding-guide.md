# Как встроить сервис входа в свой проект

Пошагово: от пустой машины до сервисов проекта, которые пускают людей по их правам и друг друга — по ключам.
Всё ниже проверено на примере магазина (`samples/shop`: «Заказы» и «Уведомления», проверка — `scripts/keycloak/check-shop.sh`);
что и почему устроено так — [этап 6а](keycloak/06a-embedding.md), что мешало и как исправлено — [журнал трений](keycloak/06a-friction.md).

## Что получится

```
                   ┌────────── сервер входа (Keycloak) ──────────┐
 человек ──вход──▶ │ проект = realm: люди, приложения, их роли   │ ◀── ключ сервиса (client_id + секрет)
                   └───────▲───────────────────────────▲────────┘
                           │ админка, кабинет, API     │ токены
                   сервис входа проекта       сервисы проекта («Заказы», «Уведомления»)
```

- **Проект** — свои люди, свои страницы входа (тема, название, логотип), свои приложения. Один сервер входа держит
  сколько угодно проектов.
- **Приложение** — то, во что входят люди и у чего есть права (`orders.view`) и роли (`customer`, `manager`). Роль =
  режим: у сотрудника-покупателя две роли, работает он в одной из них.
- **Сервис входа проекта** — админка (`/admin`), личный кабинет (`/account`) и API для сервисов проекта.
- **Ключ** — у каждого сервиса проекта свой. По нему сервис берёт токен и ходит к другим сервисам — только туда, куда
  админ выдал ему права. Чужой сервис без ключа не пройдёт.

## 1. Поставить сервис входа

Нужны образы `platform-auth-service`, `platform-auth-keycloak` и `platform-auth-realm-migrator` и Docker Compose 2.20+.
Образы каждого релиза лежат в GHCR: `ghcr.io/123kapefa/platform-auth-service:<версия>` (в `auth/.env` —
`AUTH_REGISTRY=ghcr.io/123kapefa/` и `AUTH_VERSION`, перед первым запуском — `docker login ghcr.io` с токеном GitHub,
право `read:packages`). Другой источник — реестр компании или локальная сборка `scripts/package/build-images.sh`.

```yaml
# compose.yaml проекта
include:
  - path: auth/compose.yaml          # копия deploy/compose.yaml
    env_file: auth/.env              # по образцу deploy/.env.example
```

В `auth/.env` — внешний адрес сервера входа (`AUTH_KEYCLOAK_URL`), пароли баз, сертификат для ключей DataProtection,
код проекта (`AUTH_REALM`). Без обязательных переменных compose не стартует и пишет, чего не хватает. Снаружи — только
через прокси с https: порты публикуются на `127.0.0.1`.

## 2. Создать проект

```bash
scripts/project/create-project.sh shop "Магазин" https://auth.shop.example.com   # realm и секрет входа (в AUTH_BFF_CLIENT_SECRET)
scripts/project/add-admin.py shop 79990000001 --email admin@shop.example.com      # первый админ
REALM=shop scripts/keycloak/registration-token.sh                                  # разрешение выдавать ключи → AUTH_REGISTRATION_TOKEN
```

Почта для кодов и сброса пароля — `SMTP_HOST`, `SMTP_PORT`, `SMTP_FROM`, `SMTP_USER`, `SMTP_PASSWORD` при создании
проекта. Локально без https — `DEV=1` (почта в Mailpit). Скрипты можно запускать повторно.

Оформление страниц входа (название, логотип, цвета) админ проекта настраивает в админке, раздел «Оформление».

## 3. Подключить приложение

```bash
# Приложение, куда входят люди: роль по умолчанию — у всех; --web — страница приложения (вход с неё)
scripts/project/add-app.py shop orders "Заказы" --default-role customer --web https://shop.example.com
# Внутренний сервис без людей — только права, которые выдают ключам других сервисов
scripts/project/add-app.py shop notifications "Уведомления"
```

Права и роли руками не заводят — их объявляет код приложения (шаг 4).

## 4. Сервис приложения

**Пакеты** — из GitHub Packages (`https://nuget.pkg.github.com/123kapefa/index.json`, каждый релиз; читать — с токеном
GitHub, право `read:packages`), фида компании или локального (`scripts/package/pack.sh` → `artifacts/nuget`).
Подключение — `deploy/nuget.config.example`:

```xml
<PackageReference Include="Platform.Authentication" />         <!-- проверка токенов, права, токен по ключу -->
<PackageReference Include="Platform.Authentication.Redis" />   <!-- мгновенный отзыв -->
<PackageReference Include="Platform.AuthService.Client" />     <!-- API сервиса входа, права из кода -->
```

**Настройки** — адрес realm один раз; внутренний адрес — если сервис видит сервер входа не по внешнему (в Docker):

```json
"Authentication": {
  "Authority": "https://id.shop.example.com/realms/shop",
  "InternalAuthority": "http://auth-keycloak:8180/realms/shop",
  "Audience": "orders"
},
"ConnectionStrings": { "Redis": "auth-redis:6379" },
"AuthService": { "BaseUrl": "http://auth-service:8080/" },
"ServiceToken": { "ClientId": "service-orders" }
```

Секрет ключа — файлом: `ServiceToken__ClientSecretFile=/run/secrets/orders-key` (секрет Docker/Kubernetes), или
переменной `ServiceToken__ClientSecret`. Из файла сервис берёт секрет при каждом новом токене — перевыпуск ключа без
перезапуска (шаг 8). Токен берётся только из заголовка `Authorization`; cookie сервиса входа читает только он сам.

**`Program.cs`**:

```csharp
builder.Services.AddPlatformJwtAuthentication(builder.Configuration);   // подпись, издатель, адресат
builder.Services.AddPlatformAuthorization();
builder.Services.AddPlatformPermissions();                              // права — из токена
builder.Services.AddPlatformTokenRevocation(builder.Configuration);     // блокировка, «выйти везде», смена ролей — сразу

// Права и роли — из кода: при старте сервис отправляет их в сервис входа своим ключом
builder.Services.AddAppCatalog(builder.Configuration, catalog => catalog
    .Permission("orders.view", "Свои заказы")
    .Permission("orders.manage", "Все заказы")
    .Role("customer", "Покупатель", "orders.view")
    .Role("manager", "Сотрудник", "orders.view", "orders.manage"));

app.MapGet("/api/orders", ...).RequirePermissions("orders.view");     // человек с правом
app.MapGet("/api/me", ...).RequireUser();                             // любой вошедший человек
app.MapPost("/api/notifications", ...).RequireService("notifications.send");   // только сервис по ключу с правом
```

Право, которое проверяет код, в админке не удалить; роли заводятся один раз — дальше их составом управляет админ.

## 5. Страница приложения

Готовый модуль входа — у сервиса входа проекта, своей библиотеки не нужно:

```html
<script src="https://auth.shop.example.com/sdk/platform-login.js"></script>
<script>
  const auth = PlatformLogin.create({
    authority: "https://id.shop.example.com/realms/shop",
    clientId: "orders-web",   // из add-app.py --web
    app: "orders",            // оформление приложения на страницах входа
  });
  await auth.handleRedirect();                   // вернулись со страницы входа
  if (!auth.isLoggedIn()) auth.login();          // или по кнопке
  const orders = await (await auth.fetch("/api/orders")).json();
</script>
```

`auth.fetch` сам обновляет токен, когда тот истёк или у человека изменились роли и режим (ответ `401 access_changed`).
Пример целиком — `samples/shop/Orders/wwwroot/index.html`.

## 6. Ключи: сервис сервису

1. Админка проекта → «Сервисы и ключи» → «Добавить сервис»: код `orders` (клиент `service-orders`), права в сервисе
   входа. Секрет показывается один раз — сразу в хранилище секретов.
   - `service.catalog.declare` — объявлять права и роли своего приложения (шаг 4), нужен каждому приложению с `AddAppCatalog`;
   - `service.users.read` — читать пользователя (телефон, почта, заблокирован ли);
   - `service.access.check` — есть ли у пользователя право (фоновые задачи без его токена).
2. Права в других приложениях — там же, «Права» → «В приложении «Уведомления»». Ключ «Заказов» с правом
   `notifications.send` получает токен, адресованный и «Уведомлениям»; в токене права разложены по приложениям, и
   каждый сервис видит только свои. Без такого права «Уведомления» токен ключа не примут (401).
3. В вызывающем сервисе:

```csharp
builder.Services.AddPlatformServiceToken(builder.Configuration);
builder.Services.AddHttpClient<NotificationsClient>(c => c.BaseAddress = new Uri("http://notifications:8080/"))
    .AddServiceToken();   // токен в каждый запрос; 401 — новый токен и повтор
```

То же — API: `PUT /auth/admin/services/orders/permissions` с
`{"permissions": ["service.catalog.declare", "notifications:notifications.send"]}`; что можно выдать —
`GET /auth/admin/services/grantable`.

## 7. Режимы

Человек с несколькими ролями в приложении выбирает режим сам: личный кабинет → «Профиль» → «Режим в приложениях».
Открытая страница приложения подхватит новый режим без повторного входа. Новому пользователю ничего выбирать не нужно:
пока режим не выбран, действует роль по умолчанию. API — `GET /auth/modes`, `POST /auth/mode { "app": "orders", "activeMode": "manager" }`.

## 8. Эксплуатация

- **Перевыпуск ключа.** Плановый — отметить «Плановая смена» (`{"keepTokens": true}`): выданные токены доживают свой
  срок (минуты), новый секрет кладут в файл — сервис подхватит его сам. Ключ мог утечь — без отметки: старый секрет и
  его токены перестают действовать сразу; сервис снова заработает, как только новый секрет окажется в файле.
- **Отзыв ключа** — сразу: новых токенов нет, выданные не принимаются.
- **Блокировка, «выйти везде», смена ролей** — сервисы видят сразу (через Redis сервиса входа, задержка до 2 секунд).
- **Новая версия** — поменять `AUTH_VERSION` и `docker compose up -d`, перед этим — резервные копии обеих баз.
  Мигратор базы сервиса и мигратор настроек проектов отрабатывают сами до старта сервиса: проекты, созданные на
  старой версии, получают всё новое без ручных скриптов. Порядок, откат и обновление Keycloak —
  [migrations.md](migrations.md).
- **Версия пакетов** — одна на пакеты и образы. Локальный фид получает уникальную версию каждой сборки
  (`0.1.0-dev.…`) и файл `platform-version.props` с ней — проект на локальном фиде импортирует его
  (`samples/shop/Directory.Packages.props`), иначе кэш NuGet подсунет старый пакет. `RELEASE=1 pack.sh` — версия без суффикса.

## Если вход в проекте уже свой

Порядок тот же, плюс перенос пользователей: пароли BCrypt принимаются как есть (этап 6,
[06-bcrypt-migration.md](keycloak/06-bcrypt-migration.md)), при первом входе пароль перехешируется. Сервисы меняют
проверку своего токена на `AddPlatformJwtAuthentication`, роли и права переезжают в каталог приложения из кода.
Пошагово на реальном проекте — шаг 6а.5 (education-platform), он ещё впереди.

## Частые ошибки

| Что видно | Причина |
|---|---|
| 401 у сервиса на каждый запрос | `Audience` не код приложения или токен из другого realm |
| 401 у внутреннего сервиса на запрос по ключу | ключу не выдано право в этом приложении (шаг 6) |
| 403 при старте сервиса в логе каталога | ключу не выдан `service.catalog.declare` или код ключа не совпадает с кодом приложения |
| Человек вошёл, а прав нет | у приложения нет роли по умолчанию (`add-app.py --default-role`) или в ней нет прав |
| После перевыпуска сервис не шлёт запросы | старый секрет в настройках; с секретом в файле — записать новый в файл |
| Пакет обновили, а изменений нет | одна и та же версия пакета в кэше NuGet — собирать через `pack.sh` и `platform-version.props` |
