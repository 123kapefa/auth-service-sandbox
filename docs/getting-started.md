# Запуск и проверка

Поднять сервис входа у себя, войти тестовым пользователем и посмотреть, как всё работает. Для боевой установки — [release.md](release.md).

## Запуск

Нужны Docker Compose 2.20+ и (для сборки без Docker) .NET SDK.

```bash
cp .env.example .env
docker compose up -d --build --wait
```

Первая сборка занимает несколько минут (образ Keycloak собирает плагин и тему). Остановить с удалением данных:
`docker compose down -v`.

## Адреса

| Что | Адрес | Доступ |
|---|---|---|
| Личный кабинет (на него ведёт и `/`) | http://localhost:8080/account/ | тестовый пользователь |
| Админка доступа | http://localhost:8080/admin/ | админ проекта `71234567890` |
| Демо-страница разработчика: сессия, режимы, проверка запросов | http://localhost:8080/dev/ | тестовый пользователь |
| Почта (Mailpit): письма сброса пароля и коды | http://localhost:8025 | — |
| Заглушка Госуслуг и Яндекс ID, звонков и SMS | http://localhost:8190 | — |
| Консоль Keycloak | http://localhost:8180/admin | `admin` / `admin`, realm **sandbox** |

## Тестовые пользователи

Пароль у всех `Test1234!`. Войти можно по телефону или по почте.

| Телефон | Почта | Роли | Особенность |
|---|---|---|---|
| `79990000001` | `customer@sandbox.test` | customer | обычный покупатель |
| `79990000002` | `customer-admin@sandbox.test` | customer, admin | может переключиться в режим admin |
| `79990000003` | `customer-manager@sandbox.test` | customer, manager | может переключиться в режим manager |
| `79990000004` | `blocked@sandbox.test` | customer | заблокирован |
| `71234567890` | `admin@sandbox.test` | customer, admin | сразу в режиме admin; в форме можно ввести `1234567890` |
| `70987654321` | `manager@sandbox.test` | customer, manager | сразу в режиме manager; в форме можно ввести `0987654321` |
| `79990000005` | `reset@sandbox.test` | customer | для проверки сброса пароля |
| `79990000006` | `otp@sandbox.test` | customer | включена 2FA; секрет для приложения (Base32): `ONQW4ZDCN54C233UOAWXIZLTOQWXGZLDOJSXI` |
| `79990000007` | `1c-admin@sandbox.test` | customer; в «Менеджере 1С» — admin | админ приложения «Менеджер 1С» |
| `79990000008` | `1c-operator@sandbox.test` | customer; в «Менеджере 1С» — operator | оператор приложения |

Коды на телефон в стенде не уходят по-настоящему: звонки и SMS видны на странице заглушки http://localhost:8190, коды на
почту — в Mailpit.

## Попробовать

1. Открыть http://localhost:8080 → «Войти» → `70987654321` / `Test1234!`.
2. На демо-странице `/dev/` — таблица доступа, «Обновить токен», «Выйти».
3. Войти админом (`71234567890`) в админку `/admin/`: люди, роли, журнал.
4. В кабинете `/account/` — профиль, способы входа, устройства.

Токен тестового пользователя для запросов из консоли:

```bash
TOKEN=$(curl -s -d grant_type=password -d client_id=dev-cli -d username=70987654321 -d 'password=Test1234!' \
  http://localhost:8180/realms/sandbox/protocol/openid-connect/token | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:8080/me/
```

Клиент `dev-cli` (вход по паролю) существует только на стенде.

## Проверки

Скрипты `scripts/keycloak/check-*.sh` проходят пути пользователя на живом стенде и печатают `Итого: N ✓, 0 ✗`.

```bash
scripts/keycloak/check-stand.sh      # Keycloak и realm
scripts/keycloak/check-login.sh      # вход через страницу Keycloak
scripts/keycloak/check-admin.sh      # режимы, администрирование, отзыв доступа
scripts/keycloak/check-10.sh         # способы входа и коды целиком
```

Остальные скрипты называются по теме (`check-roles.sh`, `check-code-login.sh`, `check-shop.sh`, …). Тесты:
`dotnet test` (быстрые — `--filter "Category!=Infrastructure"`), `mvn test` в `infra/keycloak/providers/code-login`,
`npm test` в `frontend/` и `login-theme/`. Подробнее — корневой [README](../README.md).

## Что дальше

[Подключить к своему проекту](embedding-guide.md) · [настроить вход](login-methods.md) · [роли и доступ](access-and-roles.md)
