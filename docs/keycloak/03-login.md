# Этап 3. Вход в приложение

**Статус:** ✅ готово · **Ветка:** `stage-3/login`

## Цель

Пользователь входит через отдельную страницу Keycloak и возвращается в приложение уже вошедшим:
сервис ведёт вход по OIDC (authorization code + PKCE), ставит cookie `accessToken`/`refreshToken`
по контракту боевого auth-service, обновляет токены с ротацией и завершает сессию. Любой сервис с
`Shared/Authentication` узнаёт пользователя по cookie `accessToken`.

## Как устроено

```
Браузер ──GET /auth/login?return_to=…──► AuthService
   │   state + PKCE verifier + return_to → зашифрованная одноразовая cookie auth_login (Path=/auth/, 10 мин)
   ◄──── 302 на страницу входа Keycloak (localhost:8180, code_challenge S256, response_mode=query)
   │ вход логином и паролем
   ──GET /auth/callback?code&state──► AuthService ──code + verifier + секрет клиента──► Keycloak (keycloak:8180)
   ◄──── cookie accessToken + refreshToken, 302 на return_to
   ──любой запрос (cookie accessToken)──► любой сервис: JwtBearer берёт токен из cookie
   ──POST /auth/refresh (cookie refreshToken)──► Keycloak: новые токены, старый refresh больше не действует
   ──POST /auth/logout──► Keycloak: сессия завершена; cookie удаляются
```

| Часть | Где |
|---|---|
| Что нужно от сервера входа (`IIdentityProvider`) | `AuthService.Core/Abstractions` — без слова Keycloak |
| Реализация через Keycloak (PKCE, обмен кода, refresh, выход) | `AuthService.Infrastructure.Keycloak` |
| Сценарии `/auth/*` — хендлер (`Result`) + эндпоинт (cookie, ответ) + валидатор | `AuthService.Core/Features/Auth/UseCases` |
| Cookie, состояние входа, проверка `return_to` | `AuthService.Core/Features/Auth/{Cookies,ReturnUrls}` |
| Токен из cookie для всех сервисов | `Shared/Authentication` (`AccessTokenCookie`) |
| Демо-страница для тестов | `AuthService.Api/wwwroot/index.html` на `/` |

## Эндпоинты

| Метод и путь | Что делает | Ответ |
|---|---|---|
| `GET /auth/login?return_to=` | уход на страницу входа | 302 на Keycloak |
| `GET /auth/callback` | обмен кода, cookie | 302 на `return_to`; 400 — нет/чужое состояние входа, 401 — отказ входа |
| `POST /auth/refresh` | новые токены в cookie | 200 `{ accessTokenExpiresAt }`; 401 — refresh не принят (cookie удаляются) |
| `POST /auth/logout` | завершить сессию в Keycloak | 200; cookie удаляются **всегда**, даже если Keycloak недоступен |

`refresh` и `logout` — только POST: cookie с `SameSite=Lax` чужой сайт в фоновый POST не подставит.

## Cookie (контракт)

| Cookie | Path | Живёт | Кто читает |
|---|---|---|---|
| `accessToken` | `/` | столько, сколько сам токен (15 мин) | все сервисы (`Shared/Authentication`) |
| `refreshToken` | `/auth/` | столько, сколько refresh (сессия Keycloak) | только эндпоинты входа |
| `auth_login` | `/auth/` | 10 минут, одноразовая | callback |

Все httpOnly, `SameSite=Lax`; `Secure` — по окружению; `Domain` — родительский домен в проде (SSO поддоменов).

Отличия от боевого сервиса: там обе cookie на `Path=/` и живут срок refresh-токена. Здесь refresh уходит
только на `/auth/`, а `accessToken` истекает вместе с токеном — браузер не шлёт сервисам просроченный JWT.

## Настройки

| Секция | Development | Docker | Production |
|---|---|---|---|
| `Keycloak:ClientId` | `sandbox-bff` (база) | так же | так же |
| `Keycloak:ClientSecret` | dev-заглушка в файле | `.env` → compose (`SANDBOX_BFF_CLIENT_SECRET`) | только переменная `Keycloak__ClientSecret` |
| `Cookies:Secure` | `false` | `false` | `true` (по умолчанию) |
| `Cookies:Domain` | — | — | `.auth-sandbox.example` |
| `ReturnUrls:AllowedOrigins` | `localhost:5080/3000/5173` | `localhost:8080/3000/5173` | приложения платформы |

У клиента `sandbox-bff` в realm разрешены только `/auth/callback` сервиса (`:8080`, `:5080`) и PKCE S256.

## Как проверить

Без Keycloak — интеграционные тесты (`LoginFlowTests`, подставной сервер входа):

```bash
dotnet test AuthService.slnx
```

На настоящей странице Keycloak — стенд и сервис подняты:

```bash
docker compose up -d --wait keycloak auth-service
```

```bash
scripts/keycloak/check-login.sh
```

Ожидается `Итого: 15 ✓, 0 ✗` (Development — `API=http://localhost:5080 scripts/keycloak/check-login.sh`).

В браузере: http://localhost:8080 → «Войти» → `0987654321` / `Test1234!` → таблица доступа, «Обновить токен», «Выйти».

## Грабли

- **Адрес выхода из контейнера.** Keycloak отдаёт `end_session_endpoint` (и `revocation_endpoint`) **внешним**
  адресом `localhost:8180` даже контейнеру, который запросил discovery по `keycloak:8180`: это «браузерные»
  адреса. Внутренними приходят только token, introspect, userinfo, certs. Выход по refresh-токену — вызов
  сервис → Keycloak, поэтому адрес берётся рядом с `token_endpoint` (`…/token` → `…/logout`). Симптом ошибки:
  выход 500, cookie удалены, но следующее «Войти» пускает без формы — SSO-сессия Keycloak жива.
- **DI проверяется при старте в Development.** Хендлеру нужен `IIdentityProvider` — без регистрации Keycloak
  сервис не стартует, даже если `/auth/*` никто не вызывает.
- **`display` перебивает `hidden`.** В демо-странице `display: grid` у блока не давал скрыть его атрибутом
  `hidden` — нужен `[hidden] { display: none !important; }`.
- **Ключи DataProtection в памяти.** После перезапуска сервиса незавершённые входы (10 минут) нужно начать
  заново; выданные токены не затрагиваются. В проде ключи нужно хранить постоянно (БД/Redis).
- **Разметка ошибок Keycloak.** Текст ошибки на странице входа (тема `keycloak.v2`) — в элементе с классом
  `kc-feedback-text`; по нему `check-login.sh` отличает «неверный пароль» от «учётная запись заблокирована».

## Ограничения (для выводов)

- После выхода уже выданный `accessToken` работает до истечения срока (15 мин): JWT проверяется без Keycloak.
  Мгновенный отзыв при блокировке — этап 4.
- Ответы 401/403 от проверки доступа — стандартные ASP.NET, без `Envelope`.

## Критерии готовности

- [x] вход через страницу Keycloak: code + PKCE, `state`, возврат на проверенный `return_to`
- [x] cookie `accessToken`/`refreshToken` по контракту; сервисы читают токен из cookie
- [x] refresh с ротацией: старый refresh-токен отклоняется
- [x] выход завершает сессию в Keycloak и удаляет cookie
- [x] демо-страница; проверено в браузере (manager и admin)
- [x] smoke `check-login.sh` — 15/15 в Docker и Development
