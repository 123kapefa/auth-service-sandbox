# Keycloak в песочнице

Цель — научиться работать с Keycloak на практике и понять, подходит ли он на замену
собственного сервиса входа. Для этого мы повторяем здесь **полный контракт** боевого
сервиса: вход по телефону (пароль и SMS-код), cookie `accessToken`/`refreshToken`,
`return_to`, роли и активный режим, блокировку, журнал, миграцию пользователей с BCrypt.

Keycloak **заменяет** OpenIddict. Прежняя реализация сохранена тегом `openiddict-baseline`.

Цели, границы и план: документ «Keycloak в песочнице auth_service: цели и план работ».

Если Keycloak для вас новый — начните с [00-keycloak-basics.md](00-keycloak-basics.md).

## Быстрый старт

```bash
cp .env.example .env
```

```bash
docker compose up -d --build --wait keycloak
```

Образ Keycloak собирается из `infra/keycloak/Dockerfile` (плагин входа по коду и тема); первая сборка — около минуты.

```bash
scripts/keycloak/check-stand.sh
```

Ожидается `Итого: 43 ✓, 0 ✗`. Остальные проверки (сервис поднят: `docker compose up -d --build --wait`):
`check-api.sh` 48, `check-login.sh` 16, `check-login-methods.sh` 22, `check-code-login.sh` 31, `check-admin.sh` 109,
`check-roles.sh` 71, `check-admin-ui.sh` 28, `check-migration.sh` 26 (нужен .NET SDK), `check-service-keys.sh` 34 (нужно
разрешение на регистрацию: `scripts/keycloak/registration-token.sh --write-env`, затем `docker compose up -d --wait`),
весь этап 6б — `check-6b.sh` 138 (кабинет, устройства, оформление, новый проект), магазин из двух сервисов (этап 6а.4) —
`check-shop.sh` 61, вход через Госуслуги и Яндекс ID (на заглушке) — `check-external-login.sh` 41, защита от накрутки
кодов (этап 10.1) — `check-code-limits.sh` 40, код на телефон звонком и запасной SMS (10.2) — `check-phone-code.sh` 24, способы входа человека (10.3) —
`check-sign-in-methods.sh` 43, вход в два шага (10.4) — `check-two-step-login.sh` 35, смена номера самим человеком (10.5) — `check-phone-change.sh` 27, предложение ключа доступа после входа по коду (10.6) — `check-passkey-offer.sh` 12, коды и способы входа проекта в API админки (10.7) — `check-code-settings.sh` 19 и `check-project-sign-in.sh` 43. Тестов: .NET — 643, плагина Keycloak (Java, в сборке образа) — 167, админки и кабинета (Vitest) — 87, темы входа (Vitest) — 46.

Стенд, поднятый до этапа 6б, после `git pull` и `docker compose up -d --build --wait` — `scripts/project/upgrade-6b.py
sandbox` (realm импортируется только в пустую базу; подробнее — [06b](06b-account-branding.md#проекты-созданные-до-6б)),
вход через Госуслуги и Яндекс ID — `scripts/project/enable-external-login.py sandbox --stub`, смена номера самим
человеком (10.5) — `scripts/project/enable-phone-change.py sandbox`, предложение ключа доступа (10.6) —
`scripts/project/enable-passkey-offer.py sandbox`, настройки входа и кодов в админке (10.7) —
`scripts/project/enable-sign-in-settings.py sandbox`.

| Что | Адрес | Доступ |
|---|---|---|---|
| Почта (Mailpit) — письма сброса пароля и коды входа | http://localhost:8025 | — |
| Заглушка Госуслуг и Яндекс ID (вход без настоящих ключей) | http://localhost:8190 | на странице входа — «Госуслуги», «Яндекс ID» |
| Админка доступа (своя, этап 4б) | http://localhost:8080/admin/ | админ проекта `1234567890`, админ «Менеджера 1С» `79990000007` |
| Админка Keycloak | http://localhost:8180/admin | `admin` / `admin`, слева выбрать realm **sandbox** |
| Discovery | http://localhost:8180/realms/sandbox/.well-known/openid-configuration | — |
| Личный кабинет (свой, этап 6б; на него ведёт и http://localhost:8080/) | http://localhost:8080/account/ | тестовый пользователь |
| Демо-страница для разработчика: сессия, режимы, проверка эндпоинтов | http://localhost:8080/dev/ | тестовый пользователь |

Тестовые пользователи (пароль у всех `Test1234!`; войти можно по телефону или по почте):

| Логин (телефон) | Почта | Роли | Особенность |
|---|---|---|
| `79990000001` | `customer@sandbox.test` | customer | обычный покупатель |
| `79990000002` | `customer-admin@sandbox.test` | customer, admin | может переключиться в режим admin |
| `79990000003` | `customer-manager@sandbox.test` | customer, manager | может переключиться в режим manager |
| `79990000004` | `blocked@sandbox.test` | customer | заблокирован |
| `71234567890` | `admin@sandbox.test` | customer, admin | сразу в режиме **admin**; в форме можно ввести `1234567890` |
| `70987654321` | `manager@sandbox.test` | customer, manager | сразу в режиме **manager**; в форме можно ввести `0987654321` |
| `79990000005` | `reset@sandbox.test` | customer | для проверки сброса пароля: после `check-login-methods.sh` пароль — новый, случайный |
| `79990000006` | `otp@sandbox.test` | customer | включена 2FA (OTP): после пароля спрашивают код. Секрет для приложения-аутентификатора (Base32): `ONQW4ZDCN54C233UOAWXIZLTOQWXGZLDOJSXI` |
| `79990000007` | `1c-admin@sandbox.test` | customer; в «Менеджере 1С» — admin | админ приложения «Менеджер 1С»: управляет только его ролями |
| `79990000008` | `1c-operator@sandbox.test` | customer; в «Менеджере 1С» — operator | токен 1С — клиент `manager-1c-cli` (ТОЛЬКО DEV) |

## Этапы

| # | Этап | Статус | Документ |
|---|---|---|---|
| 1 | Стенд и realm | ✅ | [01-stand.md](01-stand.md) |
| 2 | Токен и защищённый API | ✅ | [02-token-api.md](02-token-api.md) |
| 3 | Вход в приложение (BFF: cookie, refresh, выход) | ✅ | [03-login.md](03-login.md) |
| 3а | Способы входа: телефон или почта, сброс пароля, 2FA по желанию, passkey | ✅ | [03a-login-methods.md](03a-login-methods.md) |
| 4 | Режимы, API администрирования, события, мгновенный отзыв доступа | ✅ | [04-admin.md](04-admin.md) |
| 4а | Динамические роли и права в Keycloak, «умный рефреш», второе приложение и его админ | ✅ | [04a-roles.md](04a-roles.md) |
| 4б | Своя админка доступа (UI): админ проекта и админ приложения | ✅ | [04b-admin-ui.md](04b-admin-ui.md) |
| 4в | Свой интерфейс входа (Keycloakify): страницы входа и письма | ✅ | [04v-login-ui.md](04v-login-ui.md) |
| 5 | Спайк: вход по коду — SMS и почта (Java-плагин) | ✅ | [05-code-login.md](05-code-login.md) |
| 6 | Спайк: миграция пользователей с BCrypt | ✅ | [06-bcrypt-migration.md](06-bcrypt-migration.md) |
| 6а | Сервис входа как компонент: ключи сервисов, новый проект, поставка | 🔄 | [06a-embedding.md](06a-embedding.md), [как встроить](../embedding-guide.md) |
| 6б | Личный кабинет и оформление под проект | ✅ | [06b-account-branding.md](06b-account-branding.md) |
| 7 | Документация | идёт по ходу | — |
| 8 | Выводы и ADR: брать ли Keycloak, чего стоит, что своё | ✅ (решения приняты) | [08-conclusions.md](08-conclusions.md), [ADR](../adr/README.md) |
| 9 | Вход через Госуслуги и Яндекс ID (на заглушке; ключи — подставить) | ✅ | [09-external-login.md](09-external-login.md) |
| 10 | Вход: способы входа и коды подешевле (10.1 — защита от накрутки кодов, 10.2 — звонок вместо SMS, 10.3 — способы входа человека, 10.4 — вход в два шага, 10.5 — смена номера самим человеком, 10.6 — предложение ключа доступа после входа по коду, 10.7 — коды и способы входа проекта в API админки, 10.8 — тема входа по макетам, 10.9 — кабинет: способы входа и смена номера, 10.10 — админка: вход и коды, способы входа человека) | 🔄 | [10-code-limits.md](10-code-limits.md), [10-phone-code.md](10-phone-code.md), [10-sign-in-methods.md](10-sign-in-methods.md), [10-two-step-login.md](10-two-step-login.md), [10-phone-change.md](10-phone-change.md), [10-passkey-offer.md](10-passkey-offer.md), [10-code-settings.md](10-code-settings.md), [10-login-theme.md](10-login-theme.md), [10-account.md](10-account.md), [10-admin.md](10-admin.md), задача [#128](https://github.com/123kapefa/auth-service-sandbox/issues/128) |

Каждый этап — отдельная ветка `stage-N/...`. План, шаги и статусы — в
[задачах GitHub](https://github.com/123kapefa/auth-service-sandbox/issues) и на
[доске проекта](https://github.com/users/123kapefa/projects/4).

**Термины.** **Проект** — отдельный продукт компании со своей базой людей (интернет-магазин, соцсеть) — это realm
Keycloak; проекты друг друга не видят. **Приложение** — сайт или программа внутри проекта (сайт магазина, менеджер 1С) —
это клиент Keycloak; аккаунт на все приложения проекта один, права в каждом свои. В песочнице один проект — realm `sandbox`.

## Где что лежит

| Путь | Что это |
|---|---|
| `compose.yaml` → `keycloak`, `keycloak-postgres` | Keycloak 26.4 и его отдельная БД |
| `compose.yaml` → `redis`, `auth-migrator` | отметки мгновенного отзыва токенов; миграции базы сервиса (`efbundle`) до старта сервиса |
| `infra/keycloak/realm/sandbox-realm.json` | весь realm как код |
| `infra/keycloak/providers/code-login` | плагин Keycloak (Java): форма входа с нормализацией номера, вход по коду, проверка BCrypt-паролей из боевого сервиса, провайдеры Яндекс ID и Госуслуг (ЕСИА); собирается в `infra/keycloak/Dockerfile` |
| `infra/idp-stub/` | заглушка Яндекс ID, Госуслуг и сервиса подписи ГОСТ (сервис `idp-stub`) — вход через них без настоящих ключей |
| `scripts/keycloak/` | `check-stand.sh` — стенд, `check-api.sh` — матрица доступа, `check-login.sh` — вход через страницу Keycloak, `check-login-methods.sh` — вход по почте, сброс пароля и 2FA; `check-code-login.sh` — вход по коду из SMS и письма (код — из лога Keycloak и Mailpit); `check-admin.sh` — режимы, администрирование, сессии, мгновенный отзыв, журнал, пароль и 2FA; `check-roles.sh` — роли и права приложений, роли пользователей, умный рефреш, «Менеджер 1С» (этап 4а); `check-admin-ui.sh` — своя админка: раздача, CSP, путь админов (этап 4б); `check-migration.sh` — перенос пользователей с BCrypt-паролями (этап 6); `check-service-keys.sh`, `check-new-project.sh`, `check-deploy.sh`, `check-shop.sh` — ключи сервисов, проект с нуля, поставка, магазин из двух сервисов (этап 6а); `check-account.sh`, `check-devices.sh`, `check-branding.sh`, `check-app-branding.sh` — кабинет, устройства, оформление проекта и приложения, `check-6b.sh` — весь этап 6б (этап 6б); `check-external-login.sh` — вход через Госуслуги и Яндекс ID (этап 9); `lib.sh` — общие помощники, `master-token.sh` — токен мастер-админки изнутри контейнера |
| `login-theme/` | страницы входа Keycloak на React (Keycloakify) и письма (`src/email/`, FreeMarker) — тема `platform`, см. `login-theme/CLAUDE.md`; jar собирается в образе Keycloak |
| `frontend/` | админка доступа на `/admin` и личный кабинет на `/account` (React, FSD) — см. `frontend/CLAUDE.md`; в образ сервиса попадает сборка |
| `scripts/project/` | новый проект и его обслуживание: `create-project.sh`, `add-admin.py`, `add-app.py`; `upgrade-6b.py` — кабинет и оформление в проекте, созданном до 6б; `enable-external-login.py` — Госуслуги и Яндекс ID с ключами проекта; `enable-phone-change.py` — смена номера (10.5); `enable-passkey-offer.py` — предложение ключа доступа (10.6); `enable-sign-in-settings.py` — настройки входа и кодов в админке (10.7) |
| `samples/shop/` | новый проект на сервисе входа (этап 6а.4): «Заказы» со страницей и «Уведомления» на пакетах `Platform.*`, `setup.sh` — поднять с нуля; что мешало — [06a-friction.md](06a-friction.md) |
| `src/Shared/Authentication` | проверка токенов, политики «роль + режим», права из токена, хук мгновенного отзыва и умного рефреша (`access_changed`) — переиспользуемая библиотека |
| `src/Shared/Authentication.Redis` | отметки отзыва и версии доступа в Redis, кэш проверки, `AddPlatformTokenRevocation` |
| `src/AuthService.Core/Authorization` | константы прав сервиса (сам состав ролей — в Keycloak, с этапа 4а) |
| `src/AuthService.Core/Features/Auth` | вход, callback, refresh, выход, режим, back-channel logout; cookie и `return_to` |
| `src/AuthService.Core/Features/Admin`, `Features/Security` | API администрирования и своих сессий |
| `src/AuthService.Infrastructure.Postgres` | база сервиса: журнал `auth.audit_entries`, миграции |
| `src/AuthService.LegacyImport` | разовый перенос пользователей боевого сервиса в Keycloak (этап 6): Id, роли, блокировка, BCrypt-хэш |
| `infra/legacy/`, `scripts/legacy/seed-legacy.sh` | «боевая» база для спайка этапа 6: таблица `users` как в боевом сервисе, выдуманные пользователи (база `legacy_auth`) |
| `src/AuthService.Infrastructure.Keycloak` | клиент Keycloak: PKCE, обмен кода, refresh, выход, Admin API от служебной учётки |
| `src/AuthService.Api/wwwroot` | демо-страница для разработчика (`/dev/`); `/` ведёт в личный кабинет |
| `.env.example` → блок Keycloak | порты и dev-пароли |
