# Метрики и дашборд

Сервис входа и Keycloak отдают метрики для Prometheus; в `deploy/monitoring` лежат готовые настройки Prometheus и дашборд
Grafana «Сервис входа». Оповещения — [alerts.yml](../deploy/monitoring/alerts.yml) (правила появляются вместе с проверкой
каждого искусственным сбоем).

## Откуда берутся метрики

| Источник | Адрес | Что там |
|---|---|---|
| Сервис входа | `:9100/metrics` (настройка `Metrics:Port`, `0` — выключить) | входы и отказы, обновление токенов, выходы, запросы к Keycloak, проверка токенов и Redis, запросы ASP.NET Core |
| Keycloak | `:9000/metrics` (порт управления) | события входа Keycloak, плагин кодов (`platform_code_*`), метрики сервера |

Порты метрик **не публикуются наружу**: ни в Ingress, ни в прокси, ни на хосте (в стенде песочницы порт сервиса открыт
только на `127.0.0.1`). Prometheus ходит к ним по внутреннему адресу (сеть Docker, кластер). В метках нет токенов,
идентификаторов людей и сессий, номеров телефонов и адресов почты.

### Метрики сервиса входа

| Метрика | Метки | Смысл |
|---|---|---|
| `auth_logins_started_total` | — | начат вход: браузер отправлен на страницу Keycloak |
| `auth_logins_total` | `result` | возврат со страницы входа: `success`, `state_lost` (метка входа потеряна — человек уходит на повторный вход, не отказ), `denied`, `invalid`, `exchange_rejected`, `exchange_error` |
| `auth_refreshes_total` | `result` | `ok`, `no_session`, `rejected` (сессия закончилась), `error` |
| `auth_logouts_total`, `auth_backchannel_logouts_total` | `result` | выход; сообщения Keycloak о завершённой сессии |
| `auth_keycloak_requests_total` | `target`, `outcome` | запросы сервиса к Keycloak: куда (`token`, `keys`, `logout`, `admin`, `registration`, `other`) и чем кончились (`2xx`…`5xx`, `error`) |
| `auth_keycloak_request_duration_seconds` | `target` | время ответа Keycloak (гистограмма) |
| `platformauth_token_checks_total` | `status` | проверка токена по отметкам отзыва: `valid`, `revoked`, `access_changed`, `unavailable` |
| `platformauth_revocation_store_errors_total` | `mode`, `kind` | Redis не ответил (`connection`) или отказал (`server`: ACL, пароль); `mode` — режим при сбое |
| `platformauth_revocation_writes_total` | `kind`, `result` | записи отметок отзыва |

Пакет `Platform.Authentication` отдаёт `platformauth_*` из любого сервиса, который подключил проверку токенов и отзыв;
чтобы увидеть их в Prometheus, сервису нужен свой порт метрик.

Счётчик без событий не появляется в выдаче, пока событие не случится: в запросах — `or vector(0)`, как в дашборде.

### События входа Keycloak

`keycloak_user_events_total{event, realm, client_id, error}`: пустой `error` — успех, иначе причина отказа
(`invalid_user_credentials`, `user_disabled`, `user_temporarily_disabled`…). События: вход, выход, обмен кода, refresh, сброс и
смена пароля, вход клиента. Включается `KC_EVENT_METRICS_USER_ENABLED` (в поставке включено).

### Коды входа (плагин)

`platform_code_sent_total`, `platform_code_verified_total` (доля введённых — второе на первое), `platform_code_cost_kopecks_total`,
`platform_code_refused_total{reason}`, `platform_code_budget_alerts_total{percent}`, `platform_code_captcha_unavailable_total`;
метки `realm`, `channel`.

## Запуск на стенде песочницы

```bash
docker compose --profile monitoring up -d prometheus grafana
```

Остановить: `docker compose --profile monitoring down` (обычный `down` их не трогает: они в профиле). Prometheus — http://localhost:9090, Grafana — http://localhost:3030 (admin / admin; на общей машине задайте свой пароль:
`GRAFANA_ADMIN_PASSWORD`), папка «Сервис входа», дашборд «Сервис входа». Оба порта привязаны к `127.0.0.1`. Проверка метрик и дашборда — `scripts/keycloak/check-metrics.sh` (сама создаёт события и ждёт сбор).

## В бою

1. Prometheus (свой или общий для проекта) собирает `auth-service:9100` и `auth-keycloak:9000` по внутренним адресам —
   пример [prometheus.yml](../deploy/monitoring/prometheus.yml).
2. Дашборд: импортировать [auth-service.json](../deploy/monitoring/grafana/dashboards/auth-service.json) в Grafana (или
   положить в каталог provisioning, как в стенде: [provisioning](../deploy/monitoring/grafana/provisioning)).
3. В Kubernetes у подов сервиса и Keycloak стоят аннотации `prometheus.io/scrape`, `port`, `path`; порты метрик в Service и
   Ingress не входят (CI это проверяет). На порту метрик нет проверки доступа, поэтому его читает любой под кластера:
   закройте NetworkPolicy, разрешив 9100 и 9000 только из namespace с Prometheus. Метрики показывают объёмы входов и отказов,
   проекты и расход на коды, но не людей.

Дашборд разбит на разделы: вход и отказы (сервис), события входа Keycloak, токены и Redis, коды, процесс сервиса. В переменной
`Проект (realm)` — фильтр для панелей Keycloak и кодов.
