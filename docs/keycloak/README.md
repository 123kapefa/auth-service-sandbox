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
docker compose up -d --wait keycloak
```

```bash
scripts/keycloak/check-stand.sh
```

Ожидается `Итого: 25 ✓, 0 ✗`.

| Что | Адрес | Доступ |
|---|---|---|---|
| Почта (Mailpit) — письма сброса пароля | http://localhost:8025 | — |
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
| `1234567890` | `admin@sandbox.test` | customer, admin | сразу в режиме **admin** |
| `0987654321` | `manager@sandbox.test` | customer, manager | сразу в режиме **manager** |
| `79990000005` | `reset@sandbox.test` | customer | для проверки сброса пароля: после `check-login-methods.sh` пароль — новый, случайный |
| `79990000006` | `otp@sandbox.test` | customer | включена 2FA (OTP): после пароля спрашивают код. Секрет для приложения-аутентификатора (Base32): `ONQW4ZDCN54C233UOAWXIZLTOQWXGZLDOJSXI` |

## Этапы

| # | Этап | Статус | Документ |
|---|---|---|---|
| 1 | Стенд и realm | ✅ | [01-stand.md](01-stand.md) |
| 2 | Токен и защищённый API | ✅ | [02-token-api.md](02-token-api.md) |
| 3 | Вход в приложение (BFF: cookie, refresh, выход) | ✅ | [03-login.md](03-login.md) |
| 3а | Способы входа: телефон или почта, сброс пароля, 2FA по желанию, passkey | ⬜ | — |
| 4 | Режимы, админка, события | ⬜ | — |
| 5 | Спайк: вход по коду — SMS и почта (Java-плагин) | ⬜ | — |
| 6 | Спайк: миграция пользователей с BCrypt | ⬜ | — |
| 7 | Документация | идёт по ходу | — |
| 8 | Выводы и ADR | ⬜ | — |

Каждый этап — отдельная ветка `stage-N/...` и PR.

## Где что лежит

| Путь | Что это |
|---|---|
| `compose.yaml` → `keycloak`, `keycloak-postgres` | Keycloak 26.4 и его отдельная БД |
| `infra/keycloak/realm/sandbox-realm.json` | весь realm как код |
| `infra/keycloak/themes/sandbox` | тема страницы входа: `+` у номера и приведение номера к цифрам |
| `scripts/keycloak/` | `check-stand.sh` — стенд, `check-api.sh` — матрица доступа, `check-login.sh` — вход через страницу Keycloak, `check-login-methods.sh` — вход по почте, сброс пароля и 2FA; `lib.sh` — общие помощники |
| `src/Shared/Authentication` | проверка токенов, политики «роль + режим», права — переиспользуемая библиотека |
| `src/AuthService.Core/Authorization` | карта «роль → права» сервиса |
| `src/AuthService.Core/Features/Auth` | вход, callback, refresh, выход; cookie и `return_to` |
| `src/AuthService.Infrastructure.Keycloak` | клиент Keycloak: PKCE, обмен кода, refresh, выход |
| `src/AuthService.Api/wwwroot` | демо-страница для тестов входа (`/`) |
| `.env.example` → блок Keycloak | порты и dev-пароли |
