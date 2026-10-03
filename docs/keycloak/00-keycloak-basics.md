# Keycloak за 15 минут — на примере realm `sandbox`

Keycloak — готовый сервер идентификации (Identity Provider) с открытым кодом. Он хранит
пользователей, показывает страницу входа, проверяет пароли, ведёт сессии и выдаёт
подписанные токены по стандартам **OAuth 2.0 / OpenID Connect (OIDC)**. OpenIddict в этой
песочнице делал то же самое, но как библиотека: страницы, хранение и правила писали мы.
Keycloak — готовое приложение, которое **настраивают**, а не пишут.

Понятия ниже идут в том порядке, в каком встречаются в админке
(http://localhost:8180/admin → слева выбрать realm **sandbox**).

## 1. Realm — изолированное пространство

Realm — свои пользователи, роли, клиенты, настройки и ключи подписи. `master` — служебный
realm, там только админ Keycloak. Всё наше живёт в realm **`sandbox`**.

От realm зависит **issuer** (`iss` в токене): `http://localhost:8180/realms/sandbox`.
Приложения проверяют его, когда валидируют JWT.

*Realm settings* — общие настройки. Что задано в нашем JSON:

| Где в админке | Значение | Зачем |
|---|---|---|
| Tokens → Access token lifespan | 15 мин | короткий access-токен |
| Sessions → SSO Session Idle / Max | 30 дней | сколько живёт вход (и refresh) |
| Tokens → Revoke refresh token, reuse 0 | вкл. | ротация: каждый refresh выдаёт новый, старый сразу недействителен |
| Security defenses → Brute force detection | 5 ошибок | временная блокировка после 5 неверных паролей |
| Authentication → Policies → Password policy | 8–128 символов, не равен логину | требования к паролю |
| Localization | ru | страница входа на русском |
| Events | user + admin events, 30 дней | журнал входов и действий админов |

## 2. Users — пользователи

У пользователя (*Users*) есть:
- **ID** (UUID) — попадает в токен как `sub`;
- **Username** — логин, у нас это телефон без `+`;
- **Enabled** — выключатель: `false` = заблокирован;
- **Attributes** — поля «ключ → значения»: у нас `phone` и `active_mode`;
- **Credentials** — пароль (хэширует сам Keycloak), OTP и т.п.;
- **Role mapping** — назначенные роли;
- **Sessions** — активные входы; отсюда можно «выкинуть» пользователя.

**User profile** (*Realm settings → User profile*) — схема полей пользователя. С версии 24
Keycloak отбрасывает незнакомые атрибуты, пока их не разрешить. У нас
*Unmanaged attributes = Admin can edit*: `phone`/`active_mode` меняет только админ или
сервис, сам пользователь — нет.

## 3. Roles — роли

- **Realm roles** — общие для realm.
- **Client roles** — принадлежат конкретному клиенту.

Бизнес-роли `customer`, `manager`, `admin` — это **client roles** клиента
`sandbox-api` (*Clients → sandbox-api → Roles*). Так в токен попадают только они, без
служебных ролей Keycloak (`offline_access`, `uma_authorization`).

**Default roles** (*Realm settings → User registration*) — что получает каждый новый
пользователь. У нас это `sandbox-api: customer`.

## 4. Clients — кто может просить токены

Client — приложение, зарегистрированное в Keycloak.

| Client | Тип | Для чего |
|---|---|---|
| `sandbox-api` | bearer-only | носитель ролей и значения `aud`; сам токены не получает |
| `sandbox-bff` | confidential (есть секрет) | наш .NET-сервис: вход через браузер (Code + PKCE), refresh, logout, Admin API — этап 3 |
| `dev-cli` | public, **только dev** | токен по логину и паролю из curl для отладки |

Важные настройки (*Clients → sandbox-bff → Settings*):
- **Valid redirect URIs** — куда Keycloak может вернуть пользователя с кодом. Остальное
  отклоняется: защита от кражи кода. У нас — `/auth/callback` сервиса: `http://localhost:8080`
  (Docker) и `http://localhost:5080` (Development); *Valid post logout redirect URIs* — те же хосты.
- **Client authentication** = on → confidential, есть *Credentials → Client secret*.
  Значение берётся из `.env` (`SANDBOX_BFF_CLIENT_SECRET`) при импорте.
- **Standard flow** = вход через браузер; **Direct access grants** = логин/пароль через API
  (у `sandbox-bff` выключено).
- **Service accounts** = клиент получает токен от своего имени. У `sandbox-bff` есть роли
  `realm-management: view-users, manage-users, view-events`.
- *Advanced → PKCE Method = S256*.

## 5. Client scopes и protocol mappers — что попадает в токен

**Protocol mapper** — правило «возьми X у пользователя и положи в claim Y».
**Client scope** — набор мапперов, подключаемый к клиентам.

Scope `platform-contract` (*Client scopes → platform-contract → Mappers*):

| Маппер | Тип | Claim |
|---|---|---|
| role | User Client Role (sandbox-api) | `role: ["admin","customer"]` |
| available_roles | User Client Role (sandbox-api) | `available_roles: [...]` |
| active_mode | User Attribute | `active_mode: "customer"` |
| phone | User Attribute | `phone: "+7999…"` |
| audience | Audience | `aud: "sandbox-api"` |

Scope `openid-min` добавляет только `sub`. Встроенные scopes (profile, email, roles…)
Keycloak не создаёт, если в импорте заданы свои, поэтому токен компактный.

**Посмотреть токен без входа:** *Clients → sandbox-bff → Client scopes → Evaluate* →
выбрать пользователя → *Generated access token*. Главный инструмент отладки мапперов.

## 6. Потоки OIDC — как получают токены

| Поток | Кто | Суть |
|---|---|---|
| **Authorization Code + PKCE** | вход пользователя (этап 3) | браузер идёт на страницу Keycloak, возвращается с одноразовым `code`, сервер меняет его на токены. Пароль видит только Keycloak |
| **Refresh token** | продление сессии | обмен refresh на новую пару |
| **Client credentials** | сервис → Admin API | токен от имени приложения, без пользователя |
| Password (Direct grant) | только `dev-cli` | логин/пароль прямо в API. В проде не используют |

Все адреса есть в discovery: `authorization_endpoint`, `token_endpoint`, `jwks_uri`,
`end_session_endpoint`.

## 7. Сессии

После входа Keycloak создаёт **user session** (claim `sid`). Refresh привязан к ней.
Выход, «выкинуть» в админке или блокировка делают refresh невозможным. Access-токен
доживает свои ≤ 15 минут: приложения проверяют его локально по подписи.

## 8. Ключи и JWKS

*Realm settings → Keys* — RSA-ключ realm. Публичная часть лежит по `jwks_uri`, приватная
не покидает Keycloak. Приложения кэшируют ключи по `kid`. Ротация: новый ключ активный,
старый *passive*, пока живы подписанные им токены.

> После `docker compose down -v` realm создаётся заново с **новыми** ключами. Приложения,
> закэшировавшие старый JWKS, до 5 минут отвечают 401 — перезапустите их.

## 9. Events — журнал

*Events*: **User events** (вход, ошибка входа, выход, refresh…) и **Admin events** (кто что
поменял). У нас оба включены и хранятся 30 дней.

## 10. Расширения (SPI)

Всё, чего нет «из коробки» (вход по SMS, проверка BCrypt-хэшей), добавляется **плагинами на
Java**: jar кладётся в `/opt/keycloak/providers`. Это этапы 5 и 6; собирать будем в Docker.

## Ссылки

- Server Administration Guide: https://www.keycloak.org/docs/latest/server_admin/
- Admin REST API: https://www.keycloak.org/docs-api/latest/rest-api/
- Запуск в контейнере: https://www.keycloak.org/server/containers
- Импорт realm: https://www.keycloak.org/server/importExport
