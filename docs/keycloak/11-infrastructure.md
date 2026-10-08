# Этап 11. Инфраструктура: CI, тесты, секреты, миграции, запуск

**Статус:** ✅ готово · **Задача:** [#156](https://github.com/123kapefa/auth-service-sandbox/issues/156) (итог — [#164](https://github.com/123kapefa/auth-service-sandbox/issues/164))

Итог этапа. Вопрос этапа: чего не хватает сервису входа, чтобы его собирали, проверяли и разворачивали без ручной
работы. До этапа не было ни одного workflow, образы и пакеты собирались руками, секреты лежали в `.env`, настройки
старых проектов обновлялись разовыми скриптами, запуск — только Docker Compose. Список составлен по сравнению с
Kalmykov-Group/auth-service (`compare-kalmykov.md` в файлах проекта).

Выпустить версию и поставить её в кластер — **[release.md](../release.md)**.

## Коротко

- **Каждый PR проверяется**: сборка .NET с анализаторами, все тесты, админка, тема входа, плагин Keycloak,
  уязвимые пакеты, модель EF против миграций, утечки секретов.
- **Тесты на настоящей инфраструктуре**: Postgres, Redis и Keycloak (наш образ) тесты поднимают сами
  (Testcontainers); сотни живых проверок стенда идут в CI ночью и на тег.
- **Релиз — тегом `vX.Y.Z`**: образы в GHCR, пакеты в GitHub Packages, релиз GitHub с их перечнем; версия одна на всё.
- **Секреты — в Infisical**, утечки ловит gitleaks в CI и перед коммитом; секрет BFF и сертификат DataProtection
  меняются без потери сессий.
- **Миграции с версиями**: база сервиса — EF только вперёд, настройки проектов — нумерованные миграции realm с версией
  в самом realm; оба мигратора отрабатывают до старта сервиса.
- **Kubernetes**: Helm-чарт, по две реплики сервиса и Keycloak (общий кэш, без sticky-сессий), миграторы — хуки Helm;
  чарт проверяется установкой в kind.

## Что сделано

| # | Что | Документ | PR |
|---|---|---|---|
| 11.1 | CI на каждый PR: .NET, админка и кабинет, тема входа, плагин Keycloak, уязвимые пакеты | этот документ | [#165](https://github.com/123kapefa/auth-service-sandbox/pull/165) |
| 11.2 | Релиз по тегу: образы в GHCR, пакеты в GitHub Packages, релиз GitHub | [release.md](../release.md), [как встроить](../embedding-guide.md) | [#170](https://github.com/123kapefa/auth-service-sandbox/pull/170) |
| 11.3 | Тесты на Testcontainers: Postgres, Redis, Keycloak; отдельная задача CI | [README](../../README.md#тесты) | [#166](https://github.com/123kapefa/auth-service-sandbox/pull/166), [#169](https://github.com/123kapefa/auth-service-sandbox/pull/169) |
| 11.4 | Живые проверки стенда в CI: ночью, на тег, вручную; развёртывание из образов | [README](../../README.md#живые-проверки-стенда) | [#171](https://github.com/123kapefa/auth-service-sandbox/pull/171) |
| 11.5 | Секреты: Infisical, gitleaks в CI и pre-commit, смена секретов | [secrets.md](../secrets.md) | [#167](https://github.com/123kapefa/auth-service-sandbox/pull/167) |
| 11.6 | Миграции: мигратор настроек проектов (realm), проверка модели EF, порядок развёртывания и обновления Keycloak | [migrations.md](../migrations.md) | [#168](https://github.com/123kapefa/auth-service-sandbox/pull/168) |
| 11.7 | Kubernetes: Helm-чарт, Keycloak в двух репликах, проверка в kind | [kubernetes.md](../kubernetes.md) | [#172](https://github.com/123kapefa/auth-service-sandbox/pull/172) |
| 11.8 | Этот документ, порядок выпуска и развёртывания, ADR, сверка версии в релизе | [release.md](../release.md) | — |

## Как устроено

### Workflows

| Workflow | Когда | Что |
|---|---|---|
| CI (`ci.yml`) | push в `main`, каждый PR | **.NET**: restore, build Release (анализаторы ломают сборку), модель EF против миграций (`scripts/db/check-model.sh`), быстрые тесты, уязвимые пакеты; **Тесты на инфраструктуре**: образ Keycloak, тесты с Testcontainers; **Админка и кабинет**, **Тема входа**: `npm ci`, lint, типы, Vitest, сборка, `npm audit`; **Плагин Keycloak**: `mvn package` с JUnit |
| secrets (`secrets.yml`) | push в `main`, каждый PR | gitleaks по всей истории, найденное значение в журнал не попадает |
| Release (`release.yml`) | тег `v*.*.*`, вручную; PR со сборкой образов, пакетов или версией — без публикации | сверка версии, образы `platform-auth-service`, `platform-auth-keycloak`, `platform-auth-realm-migrator` в GHCR (версия и `latest`), пакеты `Platform.*` в GitHub Packages, релиз GitHub |
| Стенд (`stand.yml`) | ночью, тег `v*`, вручную; PR с `scripts/keycloak/` или `scripts/ci/` | стенд в compose и `check-stand`, `check-api`, `check-login`, `check-admin`, `check-6b`, `check-10`, `check-shop`, `check-migration`; развёртывание из образов — `check-deploy` |
| Kubernetes (`kubernetes.yml`) | ночью, тег `v*`, вручную; PR с чартом или `scripts/k8s/` | `helm lint` на боевых значениях и стенде; установка в kind, проверки стенда против кластера, остановка реплик Keycloak |

Упала живая проверка — в артефакте запуска (`stand-logs`, `deploy-logs`, `k8s-logs`) вывод скриптов и логи всех
контейнеров или подов.

### Версия

Одна на пакеты, образы и чарт: `VersionPrefix` в `Directory.Build.props`, `version` и `appVersion` в `Chart.yaml`,
`AUTH_VERSION` в `deploy/.env.example`. `scripts/package/check-version.sh` сверяет их (и тег) в начале релиза и в PR,
который меняет одно из этих мест: чарт с одной версией и образы с другой иначе поставили бы в кластер прежние образы.
Локальные сборки пакетов — с суффиксом `-dev.…` ([как встроить](../embedding-guide.md#8-эксплуатация)).

### Где что

| Что | Где |
|---|---|
| Workflows | `.github/workflows/` |
| Образы и пакеты, сверка версии | `scripts/package/build-images.sh`, `pack.sh`, `check-version.sh` |
| Логи для артефактов | `scripts/ci/collect-logs.sh` |
| Модель EF против миграций | `scripts/db/check-model.sh` |
| Секреты: хранилище стенда, запуск с секретами, смена | `scripts/secrets/`, `infra/infisical/`, `.gitleaks.toml`, `.pre-commit-config.yaml` |
| Миграции realm | `scripts/project/migrate-realms.py`, `scripts/project/realm-migrations/`, образ `infra/realm-migrator/` |
| Чарт и стенд в kind | `deploy/helm/platform-auth/`, `scripts/k8s/` |
| Тесты на инфраструктуре | `tests/AuthService.InfrastructureTests` |

## Решили по ходу

| Вопрос | Решение | Почему | ADR |
|---|---|---|---|
| Хранилище секретов | **Infisical** (облако или свой сервер), оператор в Kubernetes | как у Kalmykov; Vault для набора статических секретов избыточен | [0011](../adr/0011-secrets-infisical.md) |
| Миграции настроек проектов | нумерованные идемпотентные миграции, версия — атрибут realm `platform.realmVersion` | отдельной базы для учёта не нужно, версия переезжает вместе с realm | [0012](../adr/0012-realm-migrations.md) |
| Kubernetes | **Helm-чарт**, миграторы — хуки | порядок «мигратор → раскатка → мигратор realm» и значения под проект без копий манифестов | [0013](../adr/0013-release-ghcr-helm.md) |
| Реестр | **GHCR** для образов, **GitHub Packages** для NuGet; реестр компании — переменной | реестр рядом с кодом, права от репозитория, токен в CI свой | [0013](../adr/0013-release-ghcr-helm.md) |

## Как проверить

Всё проверяется в CI — на PR, ночью и на тег; руками ничего запускать не нужно. Локально — то же по частям:

| Что | Команда |
|---|---|
| Быстрые тесты, без Docker | `dotnet test --filter "Category!=Infrastructure"` |
| Тесты на инфраструктуре (нужен Docker) | `dotnet test tests/AuthService.InfrastructureTests` |
| Модель EF против миграций | `scripts/db/check-model.sh` |
| Утечки | `gitleaks git` |
| Версия | `scripts/package/check-version.sh` |
| Стенд | `docker compose up -d --build --wait`, затем `check-*.sh` ([README](README.md#быстрый-старт)) |
| Миграции realm | `scripts/keycloak/check-realm-migrations.sh` |
| Развёртывание из образов | `scripts/package/build-images.sh && scripts/keycloak/check-deploy.sh` |
| Kubernetes | `scripts/package/build-images.sh && scripts/k8s/check-kind.sh` |

Релиз проверяется сборкой без публикации в каждом PR, который его касается. Публикация по тегу на этапе ещё не
запускалась: первым будет тег `v0.1.0` ([release.md](../release.md#выпустить-версию)).

## Ограничения и что до боя

| Что | Сейчас | До боя |
|---|---|---|
| Первый релиз | публикация по тегу не запускалась | поставить `v0.1.0`, проверить релиз и видимость пакетов |
| Видимость образов и пакетов | как выставит GitHub при первой публикации | решить: открыть или читать с токеном (`read:packages`) |
| Проверки стенда и kind | ночью и на тег, на PR — только при правке самих проверок | смотреть ночные прогоны; при частых правках realm — запускать на PR |
| Infisical в Kubernetes и Ingress с https | только `helm lint` на боевых значениях, в kind их нет | первая установка в настоящий кластер |
| Postgres и Redis | внешние, чарт их не ставит (кроме стенда) | управляемые базы или свои кластеры с резервными копиями |
| Резервные копии | описаны командами `pg_dump` | расписание и проверка восстановления |
| Логи, метрики, оповещения | вне этапа | отдельным этапом (как и OpenAPI и лимиты на эндпоинтах сервиса) |
| Проект в кластере | создаётся скриптами с машины через port-forward | Job или операция в админке платформы, если проектов станет много |

## Решения

| ADR | Решение |
|---|---|
| [0011](../adr/0011-secrets-infisical.md) | Секреты — в Infisical, утечки ловит gitleaks, смена — с перекрытием |
| [0012](../adr/0012-realm-migrations.md) | Настройки проектов — нумерованными миграциями, версия — в realm |
| [0013](../adr/0013-release-ghcr-helm.md) | Релиз тегом: образы в GHCR, пакеты в GitHub Packages, в Kubernetes — Helm |
