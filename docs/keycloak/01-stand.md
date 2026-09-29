# Этап 1. Стенд и realm

**Статус:** ✅ готово · **Ветка:** `stage-1/keycloak-stand`

## Цель

Одной командой поднимать Keycloak с настроенным realm `sandbox`, чтобы стенд был
одинаковым у всех и ничего не приходилось «накликивать» в админке.

## Что сделано

- В `compose.yaml` добавлены сервисы:
  - `keycloak-postgres` — отдельная база Keycloak;
  - `keycloak` — Keycloak **26.4**, `start-dev --import-realm`, порт **8180**.
- `infra/keycloak/realm/sandbox-realm.json` — весь realm как код: настройки, роли, клиенты,
  мапперы, тестовые пользователи, журнал событий.
- `.env.example` — блок Keycloak: порт, админ, пароль БД, секрет клиента `sandbox-bff`.
- `scripts/keycloak/check-stand.sh` — автоматическая проверка (22 пункта; этап 2 добавил пользователей в служебных режимах и проверку двух адресов Keycloak, этап 3 — адреса возврата и PKCE клиента `sandbox-bff`, этап 3а — вход по почте).

### Почему так

| Решение | Причина |
|---|---|
| Отдельная БД для Keycloak | Keycloak сам управляет схемой и обновляет её при апгрейдах. Делить базу с приложением — лишний риск |
| `KC_HOSTNAME=http://localhost:8180` | от этого адреса зависит `iss`. Браузер и приложения должны видеть один адрес |
| Секрет клиента — плейсхолдер `${SANDBOX_BFF_CLIENT_SECRET}` в JSON | импорт подставляет переменные окружения. В git нет секрета, в проде значение придёт из хранилища |
| Роли — client roles `sandbox-api` | в токен не попадают служебные роли Keycloak |
| Scope `sandbox-contract` по умолчанию у всех клиентов | любой токен realm соответствует контракту |
| Events включены в JSON | журнал нужен на этапе 4, и он должен работать без ручной настройки |

## Как проверить

```bash
docker compose up -d --wait keycloak
```

```bash
scripts/keycloak/check-stand.sh
```

Ожидается `Итого: 22 ✓, 0 ✗`.

Посмотреть токен целиком:

```bash
curl -s -d grant_type=password -d client_id=dev-cli -d username=79990000002 -d 'password=Test1234!' http://localhost:8180/realms/sandbox/protocol/openid-connect/token | python3 -c 'import json,sys,base64; t=json.load(sys.stdin)["access_token"].split(".")[1]; print(json.dumps(json.loads(base64.urlsafe_b64decode(t+"="*(-len(t)%4))),indent=1,ensure_ascii=False))'
```

Ожидается примерно:

```json
{
 "iss": "http://localhost:8180/realms/sandbox",
 "aud": "sandbox-api",
 "role": ["admin", "customer"],
 "available_roles": ["admin", "customer"],
 "active_mode": "customer",
 "phone": "+79990000002"
}
```

### Пройти руками в админке (≈10 минут)

1. http://localhost:8180/admin → `admin`/`admin` → слева переключить realm на **sandbox**.
2. *Users* → `79990000002` → вкладки *Attributes*, *Role mapping*, *Sessions*.
3. *Clients → sandbox-bff → Client scopes → Evaluate* → пользователь `79990000003` →
   *Generated access token*. Сравните с таблицей мапперов в [00-keycloak-basics.md](00-keycloak-basics.md).
4. *Client scopes → sandbox-contract → Mappers → active_mode* — как атрибут становится claim.
5. *Users → 79990000001* → выключить *Enabled* → повторить curl из «Как проверить» с этим
   логином: ответ `Account disabled`. Включить обратно.
6. *Events → User events* — видны входы и ошибки, которые вы только что сделали.

## Как менять realm

Импорт выполняется **только в пустую БД** (стратегия `IGNORE_EXISTING`):

1. Поменять настройку в админке и убедиться, что работает.
2. Перенести изменение в `sandbox-realm.json`. Как настройка выглядит в JSON, подскажет
   *Realm settings → Action (справа вверху) → Partial export*.
3. Пересоздать стенд и прогнать проверку:

```bash
docker compose rm -sfv keycloak keycloak-postgres && docker volume rm auth_service_keycloak_postgres_data && docker compose up -d --wait keycloak
```

```bash
scripts/keycloak/check-stand.sh
```

## Грабли (уже учтены)

- **Default roles:** состав `default-roles-sandbox` описывается в `roles.realm`; поле
  `defaultRole` только указывает на неё. Иначе новые пользователи не получают `customer`.
- **User profile (KC ≥ 24):** без схемы Keycloak молча выбрасывает атрибуты `phone`,
  `active_mode`. Схема лежит в `components → UserProfileProvider`.
- **Свои client scopes:** если они заданы в импорте, встроенные (`profile`, `email`, `roles`,
  `basic`) не создаются. Поэтому `sub` добавляет scope `openid-min`.
- **Плейсхолдеры:** `${ИМЯ}` в JSON заменяется переменной окружения; если переменной нет,
  текст остаётся как есть. Поэтому `${username}` в схеме профиля не ломается.
- **Brute force:** 5 неверных паролей подряд блокируют пользователя до 15 минут. Сбросить:
  *Users → пользователь* → выключить переключатель *Temporarily locked* в карточке.

## Критерии готовности

- [x] стенд поднимается с нуля одной командой, без ручных шагов
- [x] discovery и JWKS отвечают, issuer корректный
- [x] токен содержит `role`, `available_roles`, `active_mode`, `phone`, `aud`
- [x] заблокированный пользователь не получает токен
- [x] журнал событий включён
