# Этап 6а. Сервис входа как компонент

**Статус:** 🔄 идёт (6а.1–6а.3 готовы) · **Ветка:** `stage-6a/embedding` · **Задача:** [#98](https://github.com/123kapefa/auth-service-sandbox/issues/98)

## Цель

Сервис входа — готовый компонент: компания заменяет им свой вход или новый проект подключает его отдельным сервисом.
Сервисы проекта обращаются к нему и друг к другу с токенами, а посторонние — не могут: доступ только по нашему
согласию, **ключом на каждый сервис проекта** (как API-ключ).

Шаги:

| Шаг | Что | Статус |
|---|---|---|
| 6а.1 | Ключи сервисов: выдача, права, перевыпуск, отзыв; API для сервисов; раздел админки «Сервисы и ключи» | ✅ |
| 6а.2 | Без «зашитой» песочницы: нейтральные имена, шаблон нового проекта и скрипты | ✅ |
| 6а.3 | Поставка: NuGet-пакеты, токен сервиса по ключу, клиент API, образы с версией, `deploy/compose.yaml` | ✅ |
| 6а.4 | Новый проект: два мини-сервиса («Заказы», «Уведомления») и страница | ⬜ |
| 6а.5 | Существующий проект: тестовая копия education-platform | ⬜ |
| 6а.6 | Руководство «Как встроить» | ⬜ |

## Ключи сервисов

Ключ — confidential-клиент Keycloak `service-{код}` только со служебной учёткой (без входа пользователей). Сервис
получает токен по `client_credentials` и ходит с ним в сервис входа и в другие сервисы проекта.

- **Выдача** — client registration API Keycloak по разрешению техника (initial access token,
  `Keycloak__RegistrationToken`, `scripts/keycloak/registration-token.sh`). Сервису входа право «управлять всеми
  клиентами» не нужно; управлять своим клиентом он может токеном управления из ответа регистрации (хранится в базе,
  зашифрован DataProtection). Не удалось выдать до конца — клиент удаляется.
- **Права** — роли клиента сервиса входа: `service.users.read` (читать пользователя), `service.access.check`
  (проверять доступ пользователя). В токене сервиса — только они, без ролей пользователя, плюс `client_id`.
- **Перевыпуск** — новый секрет задаёт сервис входа; старый перестаёт приниматься сразу.
- **Отзыв** — сначала гасятся выданные токены (мгновенный отзыв, как у пользователей), потом удаляется клиент.

API для сервисов (`RequireService`): `GET /auth/services/users/{id}`, `GET /auth/services/users?login=…`,
`GET /auth/services/users/{id}/access?permission=…`. Админка: `/auth/admin/services` (выдать, список, перевыпустить,
права, отозвать) — только для пользователя с правом `services.manage`; сервис по ключу туда не попадает.

## Новый проект

Проект — отдельный realm из шаблона (клиенты `auth-api` и `auth-bff` — одинаковые во всех проектах):

```bash
scripts/project/create-project.sh shop "Магазин" https://auth.shop.example.com   # realm, секрет auth-bff
scripts/project/add-admin.py shop 79990000001 --email admin@shop.example.com      # первый админ
scripts/project/add-app.py shop orders "Заказы" --default-role customer --web https://shop.example.com
```

В бою — https обязателен (`sslRequired external`), почта — `SMTP_HOST`, `SMTP_PORT`, `SMTP_FROM`, `SMTP_USER` и
`SMTP_PASSWORD` (STARTTLS). `DEV=1` — локальный проект без https, почта в mailpit. Скрипты можно запускать повторно:
сделанные шаги пропускаются.

## Поставка

- **NuGet-пакеты** `Platform.*` (версия — `Directory.Build.props`): `scripts/package/pack.sh` → `artifacts/nuget`,
  подключение — `deploy/nuget.config.example`. Сервису проекта: `Platform.Authentication` (проверка токенов,
  `RequireUser`/`RequireService`, токен сервиса по ключу — секция `ServiceToken`), `Platform.Authentication.Redis`
  (мгновенный отзыв), `Platform.AuthService.Client` (`AddAuthServiceClient`: адрес — `AuthService:BaseUrl`).
- **Образы** `platform-auth-service` и `platform-auth-keycloak` с той же версией: `scripts/package/build-images.sh`
  (`REGISTRY=… --push` — в реестр компании).
- **`deploy/compose.yaml`** — вставить в свой compose через `include:` со своим env-файлом по образцу
  `deploy/.env.example`. По умолчанию — боевой режим: Keycloak `start` за прокси с https (порты — на 127.0.0.1,
  `X-Forwarded-*` — только от прокси), сервис — Production, не root, ключи DataProtection зашифрованы сертификатом
  (`AUTH_DATA_PROTECTION_CERT_FILE`, без него не стартует). Без обязательных переменных compose не запускается и
  пишет, чего не хватает.

## Проверка

```bash
scripts/keycloak/check-service-keys.sh   # ключи на стенде песочницы
scripts/keycloak/check-new-project.sh    # проект с нуля (demo-shop), свой экземпляр сервиса
scripts/package/build-images.sh && scripts/keycloak/check-deploy.sh   # поставка в боевом режиме
```

`check-deploy.sh` ставит всё как чужой проект: свой compose с `include:`, прокси Caddy с https, проект без `DEV`;
входит админом, выдаёт ключ, перезапускает сервис и перевыпускает ключ (ключи DataProtection расшифрованы после
перезапуска) — и удаляет установку (`KEEP=1` — оставить).
