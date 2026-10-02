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

Ожидается `Итого: 44 ✓, 0 ✗`. Остальные проверки (сервис поднят: `docker compose up -d --build --wait`):
`check-api.sh` 48, `check-login.sh` 16, `check-login-methods.sh` 21, `check-code-login.sh` 28, `check-admin.sh` 108,
`check-roles.sh` 71; .NET-тестов — 325; тестов админки (Vitest) — 20.

| Что | Адрес | Доступ |
|---|---|---|---|
| Почта (Mailpit) — письма сброса пароля и коды входа | http://localhost:8025 | — |
| Админка доступа (своя, этап 4б) | http://localhost:8080/admin/ | админ проекта `1234567890`, админ «Менеджера 1С» `79990000007` |
| Админка Keycloak | http://localhost:8180/admin | `admin` / `admin`, слева выбрать realm **sandbox** |
| Discovery | http://localhost:8180/realms/sandbox/.well-known/openid-configuration | — |
| Личный кабинет пользователя | http://localhost:8180/realms/sandbox/account | тестовый пользователь |

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
| 4б | Своя админка доступа (UI): админ проекта и админ приложения | ⬜ | — |
| 4в | Свой интерфейс входа (Keycloakify): страницы входа и письма | ⬜ | — |
| 5 | Спайк: вход по коду — SMS и почта (Java-плагин) | ✅ | [05-code-login.md](05-code-login.md) |
| 6 | Спайк: миграция пользователей с BCrypt | ⬜ | — |
| 7 | Документация | идёт по ходу | — |
| 8 | Выводы и ADR | ⬜ | — |

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
| `infra/keycloak/themes/sandbox` | тема страницы входа: `+` у номера, кнопка «Войти по коду», экран ввода кода |
| `infra/keycloak/providers/code-login` | плагин Keycloak (Java): форма входа с нормализацией номера, вход по коду; собирается в `infra/keycloak/Dockerfile` |
| `scripts/keycloak/` | `check-stand.sh` — стенд, `check-api.sh` — матрица доступа, `check-login.sh` — вход через страницу Keycloak, `check-login-methods.sh` — вход по почте, сброс пароля и 2FA; `check-code-login.sh` — вход по коду из SMS и письма (код — из лога Keycloak и Mailpit); `check-admin.sh` — режимы, администрирование, сессии, мгновенный отзыв, журнал; `check-roles.sh` — роли и права приложений, роли пользователей, умный рефреш, «Менеджер 1С» (этап 4а); `lib.sh` — общие помощники |
| `frontend/` | админка доступа на `/admin` (React, FSD) — см. `frontend/CLAUDE.md`; в образ сервиса попадает сборка |
| `src/Shared/Authentication` | проверка токенов, политики «роль + режим», права из токена, хук мгновенного отзыва и умного рефреша (`access_changed`) — переиспользуемая библиотека |
| `src/Shared/Authentication.Redis` | отметки отзыва и версии доступа в Redis, кэш проверки, `AddPlatformTokenRevocation` |
| `src/AuthService.Core/Authorization` | константы прав сервиса (сам состав ролей — в Keycloak, с этапа 4а) |
| `src/AuthService.Core/Features/Auth` | вход, callback, refresh, выход, режим, back-channel logout; cookie и `return_to` |
| `src/AuthService.Core/Features/Admin`, `Features/Security` | API администрирования и своих сессий |
| `src/AuthService.Infrastructure.Postgres` | база сервиса: журнал `auth.audit_entries`, миграции |
| `src/AuthService.Infrastructure.Keycloak` | клиент Keycloak: PKCE, обмен кода, refresh, выход, Admin API от служебной учётки |
| `src/AuthService.Api/wwwroot` | демо-страница для тестов входа (`/`) |
| `.env.example` → блок Keycloak | порты и dev-пароли |
