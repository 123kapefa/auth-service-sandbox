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

Ожидается `Итого: 41 ✓, 0 ✗`.

| Что | Адрес | Доступ |
|---|---|---|---|
| Почта (Mailpit) — письма сброса пароля и коды входа | http://localhost:8025 | — |
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

## Этапы

| # | Этап | Статус | Документ |
|---|---|---|---|
| 1 | Стенд и realm | ✅ | [01-stand.md](01-stand.md) |
| 2 | Токен и защищённый API | ✅ | [02-token-api.md](02-token-api.md) |
| 3 | Вход в приложение (BFF: cookie, refresh, выход) | ✅ | [03-login.md](03-login.md) |
| 3а | Способы входа: телефон или почта, сброс пароля, 2FA по желанию, passkey | ✅ | [03a-login-methods.md](03a-login-methods.md) |
| 4 | Режимы, API администрирования, события, мгновенный отзыв доступа | ✅ | [04-admin.md](04-admin.md) |
| 4а | Динамические роли и права в Keycloak, «умный рефреш» | ⬜ | — |
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
| `scripts/keycloak/` | `check-stand.sh` — стенд, `check-api.sh` — матрица доступа, `check-login.sh` — вход через страницу Keycloak, `check-login-methods.sh` — вход по почте, сброс пароля и 2FA; `check-code-login.sh` — вход по коду из SMS и письма (код — из лога Keycloak и Mailpit); `check-admin.sh` — режимы, администрирование, сессии, мгновенный отзыв, журнал; `lib.sh` — общие помощники |
| `src/Shared/Authentication` | проверка токенов, политики «роль + режим», права, хук мгновенного отзыва — переиспользуемая библиотека |
| `src/Shared/Authentication.Redis` | отметки отзыва в Redis, кэш проверки, `AddPlatformTokenRevocation` |
| `src/AuthService.Core/Authorization` | карта «роль → права» сервиса |
| `src/AuthService.Core/Features/Auth` | вход, callback, refresh, выход, режим, back-channel logout; cookie и `return_to` |
| `src/AuthService.Core/Features/Admin`, `Features/Security` | API администрирования и своих сессий |
| `src/AuthService.Infrastructure.Postgres` | база сервиса: журнал `auth.audit_entries`, миграции |
| `src/AuthService.Infrastructure.Keycloak` | клиент Keycloak: PKCE, обмен кода, refresh, выход, Admin API от служебной учётки |
| `src/AuthService.Api/wwwroot` | демо-страница для тестов входа (`/`) |
| `.env.example` → блок Keycloak | порты и dev-пароли |
