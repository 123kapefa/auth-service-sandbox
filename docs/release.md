# Выпуск версии и развёртывание в Kubernetes

Этап 11.8. Пошагово: выпустить версию тегом и поставить её в кластер. Как устроены части по отдельности — CI и релиз
([этап 11](keycloak/11-infrastructure.md)), секреты ([secrets.md](secrets.md)), миграции ([migrations.md](migrations.md)),
чарт ([kubernetes.md](kubernetes.md)).

## Выпустить версию

Версия одна на всё, что выпускает релиз: NuGet-пакеты, три образа, чарт. Она записана в четырёх местах, и релиз
не соберётся, если они разошлись (`scripts/package/check-version.sh`):

| Где | Что |
|---|---|
| `Directory.Build.props` | `<VersionPrefix>` — главная, от неё версии пакетов и образов |
| `deploy/helm/platform-auth/Chart.yaml` | `version` и `appVersion` — тег образов в Kubernetes по умолчанию |
| `deploy/.env.example` | `AUTH_VERSION` — образец для `deploy/compose.yaml` |

1. **Новая версия — отдельным PR.** Поднять номер во всех местах выше (`0.1.0` → `0.2.0`; ломающее изменение API или
   настроек — следующая минорная, пока версия `0.x`). Проверка — `scripts/package/check-version.sh` печатает версию.
   В PR сам запустится workflow «Release» без публикации: образы и пакеты собираются, версии сверяются.
2. **PR влит, CI на `main` зелёный** (Actions → CI). Ночные «Стенд» и «Kubernetes» за последнюю ночь — тоже зелёные;
   если нет — чинить до тега: на тег они запустятся снова, но релиз их не ждёт.
3. **Тег** — на коммит `main`, где влит PR с версией:

   ```bash
   git switch main && git pull
   git tag -a v0.2.0 -m "v0.2.0"
   git push origin v0.2.0
   ```

   Ставит владелец репозитория или тот, у кого есть право на push тегов.
4. **Релиз** — workflow «Release» (Actions) на тег: сверяет версии (тег — тоже), собирает и отправляет образы в GHCR,
   пакеты в GitHub Packages и создаёт релиз GitHub со списком. На тот же тег запускаются «Стенд» (все проверки стенда
   и развёртывание из образов) и «Kubernetes» (чарт в kind).
5. **Проверить** — на странице релиза (Releases → v0.2.0) три образа и пакеты; образы —
   `docker pull ghcr.io/123kapefa/platform-auth-service:0.2.0`.

**Упал релиз.** Тег не совпал с версией — удалить тег (`git push origin :v0.2.0`, `git tag -d v0.2.0`), поправить
версию PR'ом, поставить тег заново. Упала сборка или отправка — после починки повторить: Actions → Release → Run
workflow, тег `v0.2.0` (образы перезапишутся, уже опубликованные пакеты пропускаются). Пакет с той же версией
опубликовать заново нельзя: исправление в пакетах — новая версия.

**Доступ к образам и пакетам.** Пакеты в GHCR и GitHub Packages получают видимость при первой публикации; если они
закрытые, читать их — с токеном GitHub с правом `read:packages`: `docker login ghcr.io`, в кластере — Secret для
скачивания образов (ниже), в NuGet — `deploy/nuget.config.example`. Открыть образы — страница пакета → Package
settings → Change visibility.

## Развернуть в Kubernetes

Чарт — из репозитория на теге версии (`git switch --detach v0.2.0`): образы по умолчанию берутся той же версии.

### Что нужно до установки

| Что | Зачем | Где подробнее |
|---|---|---|
| Кластер Kubernetes, `kubectl`, Helm 3 | | |
| Ingress-контроллер nginx (`ingress.className`) и Secret с сертификатом https на оба домена (`ingress.tlsSecretName`; cert-manager или заранее) | снаружи только https | [kubernetes.md](kubernetes.md#прокси-и-https) |
| Две базы Postgres (сервис и Keycloak) с пользователями `auth_service` и `keycloak` — пустые | схемы создают мигратор и Keycloak | `database`, `keycloakDatabase` в `values.yaml` |
| Redis | мгновенный отзыв токенов | `redis` в `values.yaml` |
| Секреты в хранилище, окружение `prod` | ниже | [secrets.md](secrets.md) |
| Оператор Infisical в кластере и учётка с Kubernetes Auth | секреты в Secret без секрета на машине | [kubernetes.md](kubernetes.md#секреты) |
| Secret для скачивания образов, если пакеты закрытые | GHCR | ниже |

**Секреты** — до установки, в Infisical (окружение `prod` проекта `auth-service`), имена — как в `values.yaml`
(`secrets`): `AUTH_DB_PASSWORD`, `AUTH_KEYCLOAK_DB_PASSWORD`, `AUTH_KEYCLOAK_ADMIN_PASSWORD`, `AUTH_BFF_CLIENT_SECRET`
(случайная строка: `python3 -c 'import secrets; print(secrets.token_urlsafe(32))'`), `AUTH_DATA_PROTECTION_CERT` и
`AUTH_DATA_PROTECTION_CERT_PASSWORD` (`scripts/secrets/new-pfx.sh`). Пароли баз — те же, что у пользователей Postgres.
Без оператора Infisical — оба Secret создать заранее с теми же ключами ([kubernetes.md](kubernetes.md#секреты)).

**Образы из закрытого GHCR:**

```bash
kubectl create namespace auth
kubectl -n auth create secret docker-registry ghcr --docker-server=ghcr.io \
  --docker-username=<логин GitHub> --docker-password=<токен с read:packages>
```

и в values — `images: { pullSecrets: [{ name: ghcr }] }`.

### Установка

1. **Values** — `my-values.yaml` по образцу в [kubernetes.md](kubernetes.md#установка): код проекта (`realm`), внешние
   адреса, базы, Redis, адреса приложений, домен cookie, Infisical, Ingress. Проверить — `helm template auth
   deploy/helm/platform-auth -f my-values.yaml > /dev/null` (пропущенное обязательное значение — ошибка с подсказкой).
2. **Поставить:**

   ```bash
   helm upgrade --install auth deploy/helm/platform-auth -n auth --create-namespace -f my-values.yaml --wait --timeout 15m
   ```

   Готово, когда `kubectl -n auth get pods` показывает по две реплики `auth-service` и `auth-keycloak` в `Running`, а
   `job/auth-migrator` — `Complete`. Сервис готов и без проекта: права в Keycloak он сверить ещё не может и через
   пару минут перестаёт пытаться (в логе — предупреждение), это лечит шаг 4.
3. **Создать проект** — теми же скриптами, что в Docker ([как встроить](embedding-guide.md#2-создать-проект)), к
   Keycloak — через port-forward, мастер-админка — изнутри пода, секреты — из хранилища:

   ```bash
   kubectl -n auth port-forward svc/auth-keycloak 8180 &
   export KEYCLOAK_EXEC="kubectl exec -i -n auth deploy/auth-keycloak --" SECRETS_ENV=prod
   scripts/secrets/run.sh scripts/project/create-project.sh shop "Магазин" https://auth.example.com http://auth-service:8080
   scripts/secrets/run.sh scripts/project/add-admin.py shop 79990000001 --email admin@example.com
   ```

   `create-project.sh` берёт секрет входа из `AUTH_BFF_CLIENT_SECRET` хранилища, поэтому сервис с ним уже совпадает;
   последний аргумент — адрес сервиса внутри кластера для выхода по сигналу Keycloak (back-channel logout).
   Разрешение на ключи сервисов — `REALM=shop scripts/keycloak/registration-token.sh` → в хранилище как
   `AUTH_REGISTRATION_TOKEN`.
4. **Перезапустить сервис** — он сверит права с новым проектом и возьмёт разрешение на ключи:
   `kubectl -n auth rollout restart deploy/auth-service`.
5. **Проверить:** `https://auth.example.com/health/ready` → `Healthy`, вход админа на `https://auth.example.com/admin/`.

### Новая версия

1. Резервные копии обеих баз ([migrations.md](migrations.md#резервные-копии) — `pg_dump` средствами вашего Postgres).
2. Чарт с нового тега (`git switch --detach v0.2.0`) и тот же `my-values.yaml`:

   ```bash
   helm upgrade auth deploy/helm/platform-auth -n auth -f my-values.yaml --wait --timeout 15m
   ```

   Мигратор базы сервиса работает до раскатки, мигратор настроек проектов — после нового Keycloak; что применено —
   `kubectl -n auth logs job/auth-migrator`, `kubectl -n auth logs job/auth-realm-migrator`.
3. Упал мигратор — `helm upgrade` падает, прежняя версия продолжает работать; причина — в логе Job. Новая версия
   Keycloak (не патч) — с `--set keycloak.updateStrategy=Recreate` ([kubernetes.md](kubernetes.md#keycloak-в-несколько-реплик)).

Откат образа без отката базы — `helm rollback auth -n auth`: схема базы сервиса совместима с прежней версией (правило
«только вперёд»). После новой версии Keycloak откат — только из резервной копии ([migrations.md](migrations.md#обновление-keycloak)).
