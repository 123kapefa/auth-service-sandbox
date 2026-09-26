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

Ожидается `Итого: 16 ✓, 0 ✗`.

| Что | Адрес | Доступ |
|---|---|---|
| Админка Keycloak | http://localhost:8180/admin | `admin` / `admin`, слева выбрать realm **sandbox** |
| Discovery | http://localhost:8180/realms/sandbox/.well-known/openid-configuration | — |
| Личный кабинет пользователя | http://localhost:8180/realms/sandbox/account | тестовый пользователь |

Тестовые пользователи (пароль у всех `Test1234!`):

| Логин | Роли | Особенность |
|---|---|---|
| `79990000001` | customer | обычный покупатель |
| `79990000002` | customer, admin | может переключиться в режим admin |
| `79990000003` | customer, manager, courier | несколько staff-ролей |
| `79990000004` | customer | заблокирован |
| `1234567890` | customer, admin | сразу в режиме **admin** |
| `0987654321` | customer, manager, courier | сразу в режиме **manager** |

## Этапы

| # | Этап | Статус | Документ |
|---|---|---|---|
| 1 | Стенд и realm | ✅ | [01-stand.md](01-stand.md) |
| 2 | Токен и защищённый API | ⬜ | — |
| 3 | Вход в приложение (BFF: cookie, refresh, выход) | ⬜ | — |
| 4 | Режимы, админка, события | ⬜ | — |
| 5 | Спайк: вход по SMS (Java-плагин) | ⬜ | — |
| 6 | Спайк: миграция пользователей с BCrypt | ⬜ | — |
| 7 | Документация | идёт по ходу | — |
| 8 | Выводы и ADR | ⬜ | — |

Каждый этап — отдельная ветка `stage-N/...` и PR.

## Где что лежит

| Путь | Что это |
|---|---|
| `compose.yaml` → `keycloak`, `keycloak-postgres` | Keycloak 26.4 и его отдельная БД |
| `infra/keycloak/realm/sandbox-realm.json` | весь realm как код |
| `scripts/keycloak/` | скрипты проверки |
| `.env.example` → блок Keycloak | порты и dev-пароли |
