# Запуск в Kubernetes

Этап 11.7. Сервис входа в кластере ставится Helm-чартом `deploy/helm/platform-auth`. Docker Compose остаётся для
одной машины (`deploy/compose.yaml`) и для разработки (`compose.yaml`).

## Что ставит чарт

Имена — от имени релиза: релиз `auth` даёт `auth-service`, `auth-keycloak` и так далее.

| Что | Как | Зачем так |
|---|---|---|
| Сервис входа (`<релиз>-service`) | Deployment, 2 реплики, PodDisruptionBudget | ключи DataProtection лежат в базе, отзыв токенов идёт через Redis, поэтому реплики равноправны |
| Keycloak (`<релиз>-keycloak`) | Deployment, 2 реплики, PodDisruptionBudget, `start` | реплики образуют кластер, подробнее ниже |
| Мигратор базы сервиса (`<релиз>-migrator`) | Job-хук: `pre-upgrade`, при установке — `post-install` | схему обновляет до раскатки новой версии |
| Мигратор настроек проектов (`<релиз>-realm-migrator`) | Job-хук `post-install`, `post-upgrade` | работает после того, как поднялся новый Keycloak |
| Ingress | https на обоих доменах, у Keycloak наружу открыты только `/realms` и `/resources` | мастер-админка снаружи не нужна |
| Секреты | оператор Infisical (`InfisicalSecret`) или Secret, созданный заранее | [secrets.md](secrets.md) |

Postgres (базы сервиса и Keycloak) и Redis внешние: адреса задаются в values. Для стенда они ставятся внутри чарта
(`stand.enabled`), данные живут, пока жив под.

### Keycloak в несколько реплик

- **Общий кэш.** Используется `KC_CACHE=ispn`. Узлы находят друг друга через базу Keycloak (`KC_CACHE_STACK=jdbc-ping`,
  таблица `JGROUPS_PING`) и соединяются напрямую по IP пода на порту 7800. Начатые входы (сессии аутентификации) и
  коды действий видит каждая реплика, поэтому sticky-сессии на Ingress не нужны: каждый шаг входа может попасть на
  любую реплику.
- **Остановка реплики.** У каждой записи кэша сессий два владельца, а сессии пользователей Keycloak 26 ещё и хранит в
  своей базе. Поэтому, если реплика уходит, даже аварийно, вошедшие пользователи остаются в системе. При штатной
  остановке (`terminationGracePeriodSeconds: 60`) реплика выходит из кластера и передаёт свою часть кэша остальным.
  При аварийной остановке кластер замечает пропажу узла не сразу: несколько десятков секунд обновление токена может
  отвечать ошибкой, после этого всё работает без повторного входа.
- **Порядок старта.** Сервис ждёт Keycloak (init-контейнер), потому что при старте сверяет с ним каталог своих прав.
- **Смена версии Keycloak** (не патча) требует `keycloak.updateStrategy=Recreate`. Разные версии в одном кластере не
  работают, а базу новая версия мигрирует при старте. Порядок описан в [migrations.md](migrations.md#обновление-keycloak).

### Пробы и ресурсы

| | startup | readiness | liveness | ресурсы по умолчанию |
|---|---|---|---|---|
| Сервис | `/health/live` | `/health/ready` (есть база и Redis) | `/health/live` | 100m / 256Mi, предел 512Mi |
| Keycloak | `/health/started` (порт 9000, первый старт со сборкой занимает минуту-две) | `/health/ready` | `/health/live` | 500m / 1Gi, предел 1536Mi |

### Миграторы

Порядок тот же, что в [migrations.md](migrations.md), только вместо `depends_on` используются хуки Helm:

1. `helm upgrade`: **`pre-upgrade`** запускает мигратор базы сервиса. Пока он не отработал, раскатки нет. Схема
   совместима с прежней версией сервиса, которая ещё работает (правило «только вперёд», [migrations.md](migrations.md#база-сервиса)).
2. Раскатываются Keycloak (базу Keycloak мигрирует он сам) и сервис.
3. **`post-upgrade`** запускает мигратор настроек проектов. Он работает после нового Keycloak, потому что миграции realm
   включают шаги его плагинов, которых у прежней версии может не быть.

При первой установке мигратор базы — хук `post-install`, потому что базы стенда создаёт сам чарт и до хука их нет.
Сервис без схемы в это время отвечает ошибками, но к нему ещё никто не ходит. Мигратору настроек проектов при
установке делать нечего: проекты создаются после (`scripts/project/create-project.sh`).

Job остаётся до следующего развёртывания, и в его логе видно, что применено:
`kubectl logs job/auth-migrator`, `kubectl logs job/auth-realm-migrator`. Если мигратор упал, `helm upgrade` тоже
падает, а прежняя версия продолжает работать. Перед развёртыванием нужны резервные копии обеих баз
([migrations.md](migrations.md#резервные-копии)).

### Прокси и https

Снаружи https держит Ingress-контроллер, до сервиса и Keycloak внутри кластера идёт http с `X-Forwarded-*`.
Заголовки принимаются только из сетей подов кластера: `service.trustedNetworks` (это `ForwardedHeaders:TrustedNetworks`)
и `keycloak.trustedProxies` (это `KC_PROXY_TRUSTED_ADDRESSES`), по умолчанию `10.0.0.0/8`. Если в вашем кластере
другая сеть подов, её нужно указать там. Для nginx чарт увеличивает буфер заголовков: у Keycloak большие cookie.

Сервис ходит к Keycloak по внутреннему адресу `http://<релиз>-keycloak:8180` за ключами и токенами, а `iss` в токенах
остаётся внешним (`publicUrls.keycloak`).

### Секреты

Источник один — хранилище (11.5, [secrets.md](secrets.md)). Оператор Infisical читает окружение и раскладывает его в
два Secret:

- `<релиз>-secrets` содержит все секреты под именами хранилища (`AUTH_DB_PASSWORD`, `AUTH_BFF_CLIENT_SECRET`…), поды
  берут их переменными окружения;
- `<релиз>-data-protection` содержит файл `certificate.pfx`, который собирается из `AUTH_DATA_PROTECTION_CERT` (PFX
  в base64). Сервис монтирует его томом (`DataProtection__CertificatePath`). Во время смены сертификата задаётся
  `dataProtection.previous=true`, тогда добавляется `previous.pfx`.

Учётка оператора входит по Kubernetes Auth, токеном ServiceAccount (`secrets.infisical.identityId`,
`serviceAccount`), так что секрета на машине нет. Без оператора оба Secret создаются заранее с теми же ключами.

## Установка

Пошагово, с тем, что нужно до установки (базы, Ingress, секреты, доступ к образам), созданием проекта и новой
версией, — [release.md](release.md#развернуть-в-kubernetes). Коротко:

```bash
helm upgrade --install auth deploy/helm/platform-auth -n auth --create-namespace -f my-values.yaml --wait --timeout 15m
```

Минимум в `my-values.yaml`:

```yaml
realm: shop                                  # код проекта
publicUrls:
  service: https://auth.example.com
  keycloak: https://id.example.com
database: { host: pg.example.internal }
keycloakDatabase: { host: pg.example.internal }
redis: redis.example.internal:6379
service:
  returnOrigins: [https://shop.example.com]
  cookies: { domain: example.com }
secrets:
  infisical: { enabled: true, identityId: <id>, serviceAccount: auth }
ingress:
  tlsSecretName: auth-tls
```

Остальное с пояснениями описано в `deploy/helm/platform-auth/values.yaml`. Образы берутся из GHCR (релиз по тегу, 11.2),
тег по умолчанию — версия чарта. После установки проект создаётся теми же скриптами, что и при Docker
([06a-embedding](keycloak/06a-embedding.md)), Keycloak для них доступен через
`kubectl port-forward svc/auth-keycloak 8180`.

## Стенд в kind

```bash
scripts/package/build-images.sh
scripts/k8s/check-kind.sh          # KEEP=1 оставит кластер
```

Скрипт создаёт кластер kind (`scripts/k8s/kind.yaml`), загружает в него образы и ставит чарт со значениями стенда
(`scripts/k8s/kind-values.yaml`): демо-проект `sandbox` импортируется из realm JSON, базы и Redis ставятся внутри чарта,
сервис и Keycloak доступны на `localhost:8080` и `localhost:8180`, как у стенда в Docker. Поэтому стенд в Docker перед
запуском нужно остановить. Скрипт проверяет:

1. обе реплики сервиса и Keycloak готовы, миграторы отработали, каждый узел Keycloak видит в кластере два узла;
2. `check-stand.sh` и `check-login.sh` проходят против кластера. Отзыв токена при выходе вторая реплика сервиса видит,
   когда истечёт её кэш проверки (`TokenRevocation:LocalCache`, 2 с), поэтому `check-login.sh` ждёт его
   (`REVOCATION_CACHE_SECONDS=2`). Мастер-админку они берут изнутри пода Keycloak
   (`KEYCLOAK_EXEC`, [master-token.sh](../scripts/keycloak/master-token.sh));
3. вошедший пользователь остаётся в системе, когда реплики Keycloak уходят по одной: сначала штатно, затем аварийно
   (`--force`), и после того, как обе заменены новыми. Обновление токена, `/me/` и вход без формы (SSO) при этом
   продолжают работать.

В CI это задача «Установка в kind» workflow `.github/workflows/kubernetes.yml`. Она запускается ночью, на тег версии,
вручную и на PR, который меняет чарт или скрипты kind. Если она упала, ресурсы и логи подов лежат в артефакте
`k8s-logs`.

Что стенд не проверяет: Ingress с https (в kind используется NodePort) и оператор Infisical (Secret создаёт сам чарт).
Их шаблоны проверяет `helm lint` в той же задаче на боевых значениях.
