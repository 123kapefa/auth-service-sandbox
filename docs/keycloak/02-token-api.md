# Этап 2. Токен и защищённый API

**Статус:** ✅ готово · **Ветка:** `stage-2/token-api`

## Цель

Сервис принимает access-токены Keycloak и пускает к эндпоинтам по двум правилам: **роль + активный режим**
(как в боевом auth-service) и **права**, которые сервис сам выводит из роли. Критерий из плана: по каждому
тестовому пользователю видно, какие эндпоинты дают 200, а какие 403.

## Как устроено

```
Keycloak ──(access-токен: role, active_mode, phone, aud, iss)──► AuthService
                                                                  │
   Shared/Authentication (переиспользуемая библиотека, будущий NuGet)
     AddPlatformJwtAuthentication   подпись RS256 по ключам из discovery, iss, aud, срок
     AddPlatformAuthorization       политики AdminOnly, ManagerOrAdmin («роль + режим»)
     AddPlatformPermissions<TMap>   права по активному режиму + CurrentUser («кто вызвал»)
                                                                  │
   AuthService.Core
     Authorization/                 карта «роль → права» этого сервиса
     Features/Me, Features/Demo     эндпоинты (IEndpoint)
```

Библиотека не знает прав конкретного сервиса: карту задаёт сервис (`AuthServiceRolePermissions`).
Другой проект подключит те же три вызова со своей картой.

### Токен

| Claim | Пример | Кто читает |
|---|---|---|
| `iss` | `http://localhost:8180/realms/sandbox` | проверка токена (`Authentication:Authority`) |
| `aud` | `sandbox-api` | проверка токена (`Authentication:Audience`) |
| `sub` | UUID пользователя | `CurrentUser.Id` |
| `role` | `["customer", "admin"]` | политики и права (`User.IsInRole`) |
| `active_mode` | `admin` | политики и права |
| `available_roles` | `["customer", "admin"]` | `CurrentUser.AvailableModes` (переключение — этап 4) |
| `phone` | `+71234567890` | `CurrentUser.Phone` |

Принимается только RS256, без допуска по времени (`ClockSkew = 0`); claims не переименовываются в URI .NET.

### Роль + активный режим

Сотрудник работает в одном режиме за раз. Политика пропускает, если `active_mode` входит в разрешённые
**и** у пользователя есть роль с тем же именем.

| Политика | Пропускает режимы |
|---|---|
| `AdminOnly` | admin |
| `ManagerOrAdmin` | admin, manager |

### Права

Права не лежат в токене — сервис выводит их из карты по **активному режиму** (админ в режиме покупателя
получает права покупателя; режим без роли не даёт ничего). Смена карты не требует перевыпуска токенов.

| Право | customer | manager | admin |
|---|---|---|---|
| `profile.view` | ✓ | ✓ | ✓ |
| `users.view` | | ✓ | ✓ |
| `users.block` | | ✓ | ✓ |
| `users.roles.manage` | | | ✓ |
| `audit.view` | | | ✓ |

В эндпоинте: `.RequireAuthorization(AuthPolicies.ADMIN_ONLY)` или `.RequirePermissions(AuthServicePermissions.Users.VIEW)`.
Без токена — 401, без права — 403.

### Два адреса Keycloak

| | Development (`dotnet run`) | Docker (контейнер) |
|---|---|---|
| `Authority` — как сервер входа называет себя (`iss`) | `http://localhost:8180/realms/sandbox` | `http://localhost:8180/realms/sandbox` |
| `MetadataAddress` — откуда сервис берёт ключи | — (из Authority) | `http://keycloak:8180/realms/sandbox/.well-known/openid-configuration` |

Браузер знает Keycloak как `localhost:8180`, и это же пишется в `iss`. Изнутри контейнера `localhost` — сам
контейнер, поэтому ключи берутся с `keycloak:8180`. Чтобы discovery, запрошенный по `keycloak:8180`, и ключи
отдавал с `keycloak:8180`, у Keycloak включено `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true` (`iss` при этом не меняется).

## Эндпоинты

| Метод и путь | Доступ |
|---|---|
| `GET /me/` | любой вошедший: данные токена и права активного режима |
| `GET /demo/admin-only/` | `AdminOnly` |
| `GET /demo/manager-or-admin/` | `ManagerOrAdmin` |
| `GET /demo/profile/` | `profile.view` |
| `GET /demo/users/` | `users.view` |
| `GET /demo/users/block/` | `users.block` |
| `GET /demo/users/roles/` | `users.roles.manage` |
| `GET /demo/audit/` | `audit.view` |

`/demo/*` — демонстрация проверок доступа; настоящие операции админки появятся на этапе 4.

## Как проверить

Без Keycloak — интеграционные тесты (сервис в памяти, токены подписаны тестовым ключом):

```bash
dotnet test AuthService.slnx
```

На настоящих токенах — стенд и сервис подняты:

```bash
docker compose up -d --wait keycloak auth-service
```

```bash
scripts/keycloak/check-api.sh
```

Ожидается `Итого: 48 ✓, 0 ✗` и матрица:

| Пользователь | `/me/` | admin-only | manager-or-admin | profile | users | users/block | users/roles | audit |
|---|---|---|---|---|---|---|---|---|
| `79990000001` покупатель | 200 | 403 | 403 | 200 | 403 | 403 | 403 | 403 |
| `79990000002` админ, режим customer | 200 | 403 | 403 | 200 | 403 | 403 | 403 | 403 |
| `79990000003` менеджер, режим customer | 200 | 403 | 403 | 200 | 403 | 403 | 403 | 403 |
| `71234567890` админ, режим admin | 200 | 200 | 200 | 200 | 200 | 200 | 200 | 200 |
| `70987654321` менеджер, режим manager | 200 | 403 | 200 | 200 | 200 | 200 | 403 | 403 |
| без токена | 401 | 401 | 401 | 401 | 401 | 401 | 401 | 401 |

Для Development (`dotnet run --project src/AuthService.Api`) — `API=http://localhost:5080 scripts/keycloak/check-api.sh`.

### Руками

```bash
TOKEN=$(curl -s -d grant_type=password -d client_id=dev-cli -d username=70987654321 -d 'password=Test1234!' http://localhost:8180/realms/sandbox/protocol/openid-connect/token | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')
```

```bash
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:8080/me/
```

## Расхождения с боевым форматом (для выводов, этап 8)

| | Боевой auth-service | Keycloak в песочнице |
|---|---|---|
| `available_roles` | строка через запятую | массив |
| `role` | роль учётной записи | все роли клиента `sandbox-api`, включая `customer` |
| Политики | роль и режим проверяются независимо | режим должен совпадать с ролью пользователя (строже) |

Потребитель, который разбирает `available_roles` как строку, на токене Keycloak сломается — при переходе
это нужно либо поменять у потребителей, либо сделать маппер Keycloak в строку.

## Грабли

- **`jwks_uri` из контейнера.** Без `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` discovery отдаёт адреса `localhost:8180`,
  недоступные изнутри контейнера, — проверка токена падает. Проверка — блок «Два адреса» в `check-stand.sh`.
- **Docker-образ и Shared-проекты.** В `Dockerfile` на этапе restore копируются `.csproj` и Shared-проектов;
  без этого `publish --no-restore` падает, как только `Api` ссылается на Shared.
- **Тест «чужого IP» / анонима в TestServer.** В тестовом сервере IP источника пустой — проверки,
  зависящие от него, тестируются на middleware напрямую (`ForwardedHeadersConfigurationTests`).
- **401/403 без Envelope.** Ответы отказа — стандартные ASP.NET (пустое тело). Единый формат — отдельная задача.

## Критерии готовности

- [x] токен Keycloak содержит `role`, `available_roles`, `active_mode`, `phone`, `aud`
- [x] сервис проверяет подпись (RS256), `iss`, `aud`, срок; чужой ключ и HS256 отклоняются
- [x] политики «роль + режим» и права по активному режиму
- [x] матрица 200/403/401 по каждому тестовому пользователю — в тестах и на настоящих токенах (Docker и Development)
- [x] проверка токенов работает из контейнера (ключи с `keycloak:8180`, `iss` — `localhost:8180`)
