# Миграции: база сервиса, настройки проектов, обновление Keycloak

Этап 11.6. При развёртывании новой версии меняются три вещи, и у каждой — свой мигратор или свой порядок:

| Что | Кто мигрирует | Где записано, что применено |
|---|---|---|
| База сервиса (журнал, ключи сервисов, устройства) | `efbundle` из образа сервиса — контейнер `auth-migrator` | таблица `__EFMigrationsHistory` |
| Настройки проектов (realm): действия, клиенты, роли, права, CSP | `migrate-realms.py` — контейнер `realm-migrator` (`auth-realm-migrator` в `deploy/compose.yaml`) | атрибут realm `platform.realmVersion` |
| База Keycloak | сам Keycloak при старте новой версии | его таблицы (`databasechangelog`) |

Оба мигратора отрабатывают и завершаются при каждом `docker compose up`; сервис стартует только после их успеха
(`depends_on: service_completed_successfully`). Повторный запуск ничего не меняет.

## Порядок развёртывания

1. **Резервные копии обеих баз** — сервиса и Keycloak (ниже). Откатить миграцию нечем, кроме копии.
2. Новая версия: `AUTH_VERSION` в env-файле (песочница — `git pull`).
3. `docker compose up -d --wait` (песочница — `--build`): Keycloak обновляет свою базу, `auth-migrator` — базу
   сервиса, `realm-migrator` (после Keycloak) — настройки всех проектов, затем стартует сервис.
4. Логи миграторов — что применено: `docker compose logs auth-migrator auth-realm-migrator` (песочница —
   `auth-migrator realm-migrator`). Мигратор упал — сервис не стартует; причина — в его логе. Исправить и снова
   `docker compose up -d --wait`: применённые миграции не повторяются.
5. Если миграция realm дала админам новое право — им войти в админку заново (мигратор пишет об этом).

### Резервные копии

`deploy/compose.yaml` (базы `auth-db` и `auth-keycloak-db`; со своим Postgres проекта — его средствами):

```bash
docker compose exec -T auth-db pg_dump -U auth_service -Fc auth_service > auth-$(date +%F-%H%M).dump
docker compose exec -T auth-keycloak-db pg_dump -U keycloak -Fc keycloak > keycloak-$(date +%F-%H%M).dump
```

Восстановить (сервис и Keycloak остановлены: `docker compose stop auth-service auth-keycloak`):

```bash
docker compose exec -T auth-db pg_restore -U auth_service -d auth_service --clean --if-exists < auth-….dump
docker compose exec -T auth-keycloak-db pg_restore -U keycloak -d keycloak --clean --if-exists < keycloak-….dump
```

В песочнице — сервисы `postgres` (`-U "$POSTGRES_USER" "$POSTGRES_DB"` из `.env`) и `keycloak-postgres`. Копия
Keycloak — вместе с копией сервиса: realm, пользователи и их сессии там, а журнал и ключи сервисов — в базе сервиса.

## База сервиса

Миграции EF Core — `src/AuthService.Infrastructure.Postgres/Migrations`, в образ сервиса попадают как `efbundle`.

- **Новая миграция** — после правки сущностей:
  `dotnet ef migrations add <Название> --project src/AuthService.Infrastructure.Postgres --startup-project src/AuthService.Infrastructure.Postgres`.
- **Модель не разошлась с миграциями** — `scripts/db/check-model.sh` (`dotnet ef migrations has-pending-model-changes`),
  запускается в CI: правка сущностей без миграции — красная сборка.
- **Только вперёд.** Применённую миграцию не правят и не удаляют; откат — новой миграцией, которая возвращает как
  было (`Down` в бою не запускается). Данные испорчены — восстановление из копии.
- **Совместимость со старой версией.** Пока идёт развёртывание (и при откате образа), со схемой работает и прежняя
  версия сервиса. Удалить или переименовать колонку — в две версии: сначала код перестаёт её читать, следующей версией —
  миграция удаляет. Новая колонка — со значением по умолчанию или допускает `NULL`.

## Настройки проектов (realm)

Realm импортируется из JSON только в пустую базу Keycloak, поэтому изменения шаблона
(`infra/keycloak/realm/sandbox-realm.json`) получают только новые проекты. Проектам, созданным раньше, — нумерованные
миграции `scripts/project/realm-migrations/NNNN-коротко.py`:

| № | Что | Было |
|---|---|---|
| `0001-stage-6b` | действия кабинета (смена почты — после письма), «вернуться» — в кабинет, оформление: роль `manage-branding` и право `branding.manage` | `upgrade-6b.py` |
| `0002-stage-10` | смена номера, предложение ключа, настройки входа: роль `manage-sign-in` и право `sign-in.manage`, капча в CSP | `upgrade-10.py` |

**Как работает.** Мигратор (`scripts/project/migrate-realms.py`) проходит по всем realm Keycloak. Проект — realm с
клиентом `auth-api` (в песочнице `sandbox-api`); остальные и `master` не трогаются. Версия проекта — атрибут realm
`platform.realmVersion`; нет атрибута — проект старше миграций (версия 0). Недостающие миграции применяются по порядку,
после каждой версия записывается: прервался на третьей — две первые не повторятся. Новые проекты (`new-project.py`)
получают последнюю версию сразу, realm песочницы — из JSON. Версия realm новее мигратора (развернули старую версию
образа) — проект пропускается, не ошибка. Один проект не обновился — остальные обновляются, мигратор завершается с
кодом 1, сервис не стартует.

```bash
scripts/project/migrate-realms.py --status        # версии проектов, ничего не меняя
scripts/project/migrate-realms.py                 # вручную — то же, что контейнер при развёртывании
scripts/project/migrate-realms.py demo-shop       # только эти проекты
```

Контейнер берёт токен мастер-админки сам (`KC_ADMIN_DIRECT=1`: пароль `KEYCLOAK_ADMIN_PASSWORD`, адрес Keycloak в сети
Docker — `KC`); с машины — как остальные скрипты (`scripts/keycloak/master-token.sh`).

**Новая миграция** — когда меняется шаблон realm так, что старым проектам нужно то же (действие, клиент, маппер, роль,
право, сценарий входа):

1. Файл `scripts/project/realm-migrations/NNNN-коротко.py` со следующим номером: строка документации (что и зачем) и
   `apply(admin)` → строка-итог для лога (`admin` — `keycloak_admin.Admin` проекта). Готовые шаги — `enable-*.py`
   (`load_script`), как в `0002-stage-10`.
2. То же изменение — в `sandbox-realm.json`, и там же `attributes."platform.realmVersion"` — новый номер.
3. Проверка — `scripts/keycloak/check-realm-migrations.sh`: realm песочницы откатывается к виду «до 6б», контейнер
   мигратора применяет все миграции, повторный запуск ничего не меняет. Новую миграцию — добавить в откат и в итоговую
   строку проверки.

**Правила.** Миграция **идемпотентна**: создаёт, если нет (`409` — уже есть), включает, если выключено, — её можно
применить к проекту, где это уже сделано руками или шаблоном. Применённую миграцию не правят (её не применят
повторно): исправление — следующей. Не трогает то, что у каждого проекта своё: название, оформление, ключи Госуслуг и
Яндекс ID (`enable-external-login.py`), приложения проекта. Свои значения проекта не затирает (как CSP в `0002`).

## Обновление Keycloak

Плагин и тема собраны под конкретную версию Keycloak (26.4.7; [ADR 0006](adr/0006-keycloak-plugins.md)): часть его SPI
внутренняя, совместимость между версиями не обещана. Порядок перехода:

1. **Версия** — в двух местах вместе: `FROM quay.io/keycloak/keycloak:<версия>` в `infra/keycloak/Dockerfile` и
   `keycloak.version` в `infra/keycloak/providers/code-login/pom.xml`; тег образа стенда в `compose.yaml`.
2. **Сборка и тесты плагина** — `docker compose build keycloak`: Maven собирает плагин под новую версию и прогоняет его
   тесты (упал тест — упала сборка), Keycloakify — тему. Ошибки компиляции — изменения SPI, смотреть заметки к выпуску
   (migration guide) Keycloak.
3. **Стенд с чистой базой** — `docker compose down -v && docker compose up -d --build --wait`, все проверки: `check-6b.sh`,
   `check-10.sh`, `check-realm-migrations.sh` и остальные `check-*.sh` ([keycloak/README.md](keycloak/README.md)).
4. **Стенд со старой базой** — так пойдёт бой: поднять стенд на прежней версии, затем новую без `down -v`; Keycloak
   мигрирует свою базу при старте, проверки — снова.
5. **Бой** — резервная копия базы Keycloak (и сервиса), затем развёртывание по порядку выше. Миграция базы Keycloak
   необратима: откат образа на старую версию на новой базе не работает — только восстановление из копии.
