# Метрики, логи и трассировка

Сервис входа и Keycloak отдают метрики для Prometheus; в `deploy/monitoring` лежат готовые настройки Prometheus и дашборд
Grafana «Сервис входа», а в [alerts.yml](../deploy/monitoring/alerts.yml) и [alertmanager.yml](../deploy/monitoring/alertmanager.yml) — правила оповещений и маршруты.

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

## Логи и сквозная трассировка

Запрос проходит браузер → сервис входа → Keycloak. У него один **идентификатор трассы** (32 hex-символа, формат W3C): сервис создаёт его
(или продолжает, если пришёл заголовок `traceparent`) и передаёт Keycloak тем же заголовком. По нему находятся записи обеих сторон.

| Где виден идентификатор | Как |
|---|---|
| заголовок любого ответа сервиса | `X-Trace-Id` (в том числе у 401, 403 и 503) |
| тело ответа с ошибкой | `"traceId": "…"` рядом с `isError` — его называет человек в обращении в поддержку |
| журнал сервиса | строка JSON в stdout, в `Scopes` — `TraceId` и `SpanId`, код проекта `Project`, `RequestId` |
| журнал Keycloak | строка JSON, в `mdc.traceId` |

**Журнал сервиса** — одна строка JSON на запрос (`AuthService.Request`: метод, шаблон маршрута, статус, время) и записи самого сервиса.
В строке нет ни строки запроса, ни идентификаторов в пути (маршрут — шаблон: `/auth/admin/users/{userId}`), ни токенов, кодов, паролей и
номеров телефонов; здоровье, метрики, статические файлы и 404 от сканеров не пишутся, а отказ в доступе (401, 403, 429), сбой (5xx) и
необработанное исключение — пишутся. Отказ защиты от CSRF (запрос с cookie токена без `X-Requested-With`) пишет сама защита (Warning).
Пример поиска:

```bash
docker compose logs auth-service keycloak | grep 4bf92f3577b34da6a3ce929d0e0e4736
```

В Kubernetes и в сборщике логов (Loki, Elastic) — поиск по полю `Scopes[].TraceId` (сервис) и `mdc.traceId` (Keycloak).

> Журналы ASP.NET Core на уровне Information пишут адрес запроса целиком, со строкой запроса: в ней одноразовый `code` входа и `state`.
> Поэтому в `appsettings` для категорий `Microsoft.AspNetCore.Hosting.Diagnostics` и `Microsoft.AspNetCore.Http.Result` стоит `Warning`
> в каждой конфигурации; при отладке не поднимайте эти категории до Information.

**Spans** (запрос сервиса, запрос сервиса к Keycloak, обработка в Keycloak) уходят в коллектор по OTLP, когда задан адрес:

| Что | Compose (`deploy/compose.yaml`, стенд) | Helm |
|---|---|---|
| адрес коллектора OpenTelemetry (`http://otel-collector:4317`) | `AUTH_OTEL_ENDPOINT` | `tracing.otlpEndpoint` |
| какие трассы пишет Keycloak (`parentbased_always_on` — те, что начал сервис) | `AUTH_TRACING_SAMPLER` (по умолчанию `always_off`) | `tracing.sampler` |

Без адреса spans никуда не уходят, а идентификатор трассы в логах и ответах есть всегда. Что учесть в бою:

- **Доля трасс.** Сервис пишет в коллектор все запросы (`parentbased_always_on`), чтобы уменьшить: `OTEL_TRACES_SAMPLER=parentbased_traceidratio`
  и `OTEL_TRACES_SAMPLER_ARG=0.1`. Решение о записи принимает сервис; Keycloak (`tracing.sampler`) следует за ним. Флаг «записывать»
  в `traceparent` от внешнего клиента доверяется: ограничьте объём на коллекторе.
- **Что попадает в spans и зачем коллектор.** Spans сервиса входа без значений параметров запроса и без данных людей. А Keycloak пишет в свои
  spans события входа с логином (телефон или почта), IP, идентификаторами человека и сессии, адресом со строкой запроса и user-agent; отключить это
  настройками нельзя. Поэтому и сервис, и Keycloak шлют трассы **в коллектор OpenTelemetry**, который вырезает эти поля и передаёт дальше в
  Jaeger или Tempo: [otel-collector.yaml](../deploy/monitoring/otel-collector.yaml). Слать Keycloak напрямую в хранилище нельзя. Что
  осталось после коллектора, проверяет `check-tracing.sh` (раздел 5: коды, state, пароли, телефоны, токены, IP, логины в трассах Jaeger).
  Текст SQL и вызовы кэша Keycloak отключены (`KC_TRACING_JDBC_ENABLED`, `KC_TRACING_INFINISPAN_ENABLED`). Коллектор и хранилище трасс всё
  равно закрывайте от внешнего доступа.
- **OTLP без TLS.** `http://collector:4317` годится только внутри доверенной сети (Compose, кластер); для коллектора вне её — `https://` и заголовки
  авторизации из хранилища секретов (`OTEL_EXPORTER_OTLP_HEADERS`).
- **Журнал Keycloak** содержит события с пользователем и IP (`type="LOGIN_ERROR"`, `username`, `ipAddress`) — как и раньше; идентификатор трассы связал их
  с записью сервиса. Доступ и срок хранения журналов — как у персональных данных. В стенде песочницы коллектор и Jaeger поднимаются в профиле
`monitoring` (`http://localhost:16686`); `scripts/keycloak/check-tracing.sh` сам пересоздаёт сервис и Keycloak с отправкой трасс,
находит запрос в Jaeger, проверяет, что в трассах нет секретов и данных людей, и возвращает всё обратно.

## Оповещения

Правила — [alerts.yml](../deploy/monitoring/alerts.yml), получатели и маршруты — [alertmanager.yml](../deploy/monitoring/alertmanager.yml) (пример: письма,
дежурному — отдельным маршрутом; рядом — заготовка для webhook в чат). Prometheus подключает правила (`rule_files`) и знает Alertmanager
(`alerting`) — [prometheus.yml](../deploy/monitoring/prometheus.yml). Важность: **critical** — вход не работает или не работает защита (будить), **warning** —
разобраться в рабочее время, в том числе подозрение на перебор. Пороги — для проекта с десятками-сотнями входов в час; подберите под свою нагрузку.

| Оповещение | Важность | Когда | Что делать |
|---|---|---|---|
| `AuthServiceDown` | critical | Prometheus не собирает метрики сервиса > 1 мин | сервис упал или недоступен по сети: перезапуск, журнал |
| `AuthKeycloakDown` | critical | не собираются метрики Keycloak > 1 мин | Keycloak упал: новые входы не проходят |
| `AuthKeycloakErrors` | critical | > 20 % запросов сервиса к Keycloak за 5 мин — 5xx или обрыв (≥ 10 запросов) | журнал Keycloak; `auth_keycloak_requests_total` по целям |
| `AuthDatabaseUnavailable` | critical | проверка готовности базы не проходит > 1 мин | Postgres сервиса: журнал, диск, соединения |
| `AuthRedisUnavailable` | critical | проверка готовности Redis не проходит > 1 мин | отзыв токенов не работает (режим «открыто») или 503 (режим «закрыто») |
| `AuthRevocationStoreErrors` | warning | ошибки от Redis при проверке токенов (`kind=server` — ACL или пароль, Redis «жив») | права пользователя Redis ([environments.md](environments.md#redis-пользователи)) |
| `AuthTokenChecksUnavailable` | critical | запросы отклонены: проверить отзыв токена нельзя (режим «закрыто») | вернуть Redis |
| `AuthLoginFailuresSpike` | warning | > 30 неудачных попыток входа за 5 мин в проекте | подбор паролей или кодов: прокси, блокировки |
| `AuthLoginFailureShareHigh` | warning | > 50 % попыток входа за 15 мин неудачны (≥ 20 попыток) | перебор — или сломан способ входа (письма, провайдер, тема) |
| `AuthRefreshFailures` | warning | > 30 % обновлений токенов за 15 мин не прошли (≥ 20) | сессии заканчиваются, или Keycloak недоступен |
| `AuthRefreshKeycloakErrors` | critical | ≥ 5 обновлений за 5 мин закончились ошибкой Keycloak | Keycloak: люди теряют сессию через 15 мин |
| `AuthCodeVerifiedShareLow` | warning | введено < 40 % отправленных кодов за час (≥ 20 отправлено) | коды не доходят (провайдер) или накрутка отправок |
| `AuthCodeBudgetNearlyExhausted` | warning | израсходовано 80 % дневного бюджета на коды | необычный расход — накрутка |
| `AuthCodeBudgetExhausted` | critical | бюджет исчерпан: SMS и звонки отключены до полуночи | увеличить бюджет или разобраться с расходом |
| `AuthCaptchaUnavailable` | warning | SmartCaptcha не отвечает | поведение — `captcha-on-outage` |
| `AuthMetricsMissing` | warning | пропала метрика готовности базы или Redis (> 5 мин) | цель убрана из `prometheus.yml`, порт метрик закрыт, сервис не стартовал |
| `AuthDependencyChecksStale` | critical | проверки готовности базы и Redis не выполнялись > 2 мин | проверка зависла: `auth_dependency_up` показывает старое значение; перезапуск сервиса |
| `Watchdog` | — | горит всегда | см. «Если мониторинг сам не работает» |

Оповещения о бюджете горят 10 минут после порога, а состояние (80 %, SMS выключены) держится до полуночи, поэтому письма «RESOLVED» по ним не отправляются (маршруты `*-once`).
Счётчики плагина кодов (`platform_code_*`) создаются нулями при старте Keycloak для всех проектов (и при первом событии нового проекта): иначе ряд появлялся бы
сразу со значением 1, а Prometheus не видит приращения у ряда без предыдущей точки — первое оповещение о бюджете (оно приходит раз в сутки) терялось бы.

**Если мониторинг сам не работает.** Правила молчат, когда лежат Prometheus или Alertmanager или нет канала до получателя. Поэтому есть оповещение `Watchdog`: оно горит
всегда, а маршрут `watchdog` в [alertmanager.yml](../deploy/monitoring/alertmanager.yml) передаёт его каждые 5 минут внешнему «сторожу» (dead man's switch:
Healthchecks.io, Better Stack, Grafana OnCall и т. п.), который поднимает тревогу, когда сигнал пропал. Адрес сторожа с токеном — из хранилища секретов
(`url_file`). Без внешнего сторожа остальные оповещения не защищены от падения самого мониторинга. На стенде `Watchdog` никуда не уходит.

**Безопасность Alertmanager.** У его API (`:9093`) нет проверки доступа: любой, кто до него дотянется, может создать тишину (silence) или подбросить оповещение. На стенде порт
привязан к `127.0.0.1`; в бою не публикуйте его наружу и держите во внутренней сети (за прокси с проверкой, если нужен интерфейс).

Alertmanager подавляет оповещения-следствия: пока горит `AuthServiceDown` или `AuthKeycloakDown`, письма об ошибках обновления и доле неудачных входов не уходят
(человек уже разбужен; `AuthKeycloakDown` при упавшем сервисе всё равно придёт отдельным письмом); при `AuthRedisUnavailable` не пишется `AuthRevocationStoreErrors`. Повтор письма — раз в 4 часа (critical — раз в час), пока горит.

**Как проверено** (`scripts/keycloak/check-alerts.sh`): (1) `promtool check rules` и **модульные тесты** `alerts_test.yml` на искусственных рядах — у каждого правила
есть ряд, при котором оно срабатывает, и (где есть порог) ряд, при котором молчит; (2) рабочие Prometheus и Alertmanager стенда загрузили правила;
(3) **живые сбои на стенде**: перебор паролей, коды без ввода, остановка Redis, базы, Keycloak и сервиса, исчерпание бюджета кодов — для каждого
оповещения проверено, что оно загорелось, письмо дошло в Mailpit (`http://localhost:8025`) и оно погасло, когда сбой убрали; подавление следствий тоже.
Для живой проверки временный Prometheus опрашивает раз в 2 секунды, а «`for`» правил сокращён до 4 секунд. Режим «закрыто» (`AuthTokenChecksUnavailable`: Redis остановлен, сервис
отвечает 503) и недоступная SmartCaptcha (`AuthCaptchaUnavailable`: адрес капчи не существует) тоже проверены живыми сбоями; «зависшая» база (пауза контейнера) —
оповещением `AuthDatabaseUnavailable`. Только модульным тестом проверено `AuthDependencyChecksStale`: метка времени проверок застывает лишь при дефекте самой публикации
(зависшая база даёт не «застыло», а «недоступна»).

В Kubernetes: правила из `alerts.yml` загружаются в Prometheus Operator ресурсом `PrometheusRule` (то же содержимое `groups`), маршруты — в `AlertmanagerConfig`
или секрет Alertmanager.

## Запуск на стенде песочницы

```bash
docker compose --profile monitoring up -d prometheus grafana
```

Alertmanager — http://localhost:9093 (письма — в Mailpit, http://localhost:8025). Остановить: `docker compose --profile monitoring down` (обычный `down` их не трогает: они в профиле). Prometheus — http://localhost:9090, Grafana — http://localhost:3030 (admin / admin; на общей машине задайте свой пароль:
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
