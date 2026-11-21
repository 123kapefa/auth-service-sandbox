#!/usr/bin/env python3
"""
Мигратор настроек проектов (realm): нумерованные миграции из realm-migrations/ применяются ко всем проектам на
Keycloak, которым их ещё не применяли. Применённая версия — в атрибуте realm platform.realmVersion; нет атрибута —
проект старше миграций (версия 0), получает все. Новые проекты получают последнюю версию из шаблона (new-project.py).
Миграции идемпотентны: прерванную можно применить ещё раз. Повторный запуск мигратора ничего не меняет.
Проектом считается realm с клиентом auth-api (в песочнице — sandbox-api); остальные realm не трогаются. Realm master —
не проект: при каждом запуске (без списка проектов) ему включается защита от перебора пароля мастер-админа (harden_master).

Запускается при каждом развёртывании (контейнер realm-migrator, infra/realm-migrator) после Keycloak и до сервиса;
вручную — с машины, где поднят Keycloak (токен мастер-админки — как у остальных скриптов):
  scripts/project/migrate-realms.py                # все проекты
  scripts/project/migrate-realms.py demo-shop      # только эти
  scripts/project/migrate-realms.py --status       # версии проектов, ничего не меняя
Код выхода — 1, если хоть один проект не обновился (остальные обновляются).
Новая миграция — файл NNNN-коротко.py со следующим номером и функцией apply(admin) → строка-итог; то же изменение —
в шаблоне infra/keycloak/realm/sandbox-realm.json и в его атрибуте platform.realmVersion (docs/migrations.md).
"""
import argparse
import re
import sys
from pathlib import Path

from keycloak_admin import Admin, load_script, master_token, realms

ATTRIBUTE = "platform.realmVersion"
DIR = Path(__file__).with_name("realm-migrations")
PROJECT_CLIENTS = ("auth-api", "sandbox-api")


def migrations():
    """[(номер, файл)] по порядку; номера — 1, 2, 3… без пропусков и повторов."""
    found = sorted((int(m.group(1)), path) for path in DIR.glob("*.py")
                   if (m := re.fullmatch(r"(\d{4})-[a-z0-9-]+\.py", path.name)))
    numbers = [n for n, _ in found]
    if numbers != list(range(1, len(found) + 1)):
        raise SystemExit(f"Миграции realm-migrations/ — по порядку с 0001 без пропусков: {numbers}")
    return found


def latest():
    return len(migrations())


def version(admin):
    """Версия настроек проекта; None — не проект сервиса входа."""
    if not any(admin.client(c) for c in PROJECT_CLIENTS):
        return None
    attributes = admin.call("GET", "").get("attributes") or {}
    return int(attributes.get(ATTRIBUTE) or 0)


def set_version(admin, number):
    """PUT realm с attributes заменяет все его атрибуты (оформление, настройки входа и кодов) — пишем поверх текущих."""
    attributes = admin.call("GET", "").get("attributes") or {}
    admin.call("PUT", "", {"attributes": {**attributes, ATTRIBUTE: str(number)}})


# Защита от перебора пароля мастер-админа: после 5 неверных паролей подряд вход блокируется на минуту, при повторах — до
# 15 минут (временная блокировка, не постоянная: постоянная давала бы тому, кто знает логин, запереть админа насовсем).
# Снаружи master закрыт прокси (docs/environments.md, «Прокси»); это вторая линия — на случай ошибки в прокси.
MASTER_BRUTE_FORCE = {
    "bruteForceProtected": True, "permanentLockout": False, "failureFactor": 5,
    "waitIncrementSeconds": 60, "maxFailureWaitSeconds": 900, "minimumQuickLoginWaitSeconds": 60,
    "quickLoginCheckMilliSeconds": 1000, "maxDeltaTimeSeconds": 43200,
}


def harden_master(admin):
    """Включает защиту от перебора у realm master. Идемпотентна; PUT с частью полей меняет только их."""
    current = admin.call("GET", "")
    if all(current.get(key) == value for key, value in MASTER_BRUTE_FORCE.items()):
        return "master: защита от перебора пароля уже включена"
    admin.call("PUT", "", MASTER_BRUTE_FORCE)
    return "master: защита от перебора пароля мастер-админа включена (5 неверных паролей — блокировка от минуты до 15 минут)"


def migrate(admin, steps):
    """Недостающие миграции проекта по порядку; печатает, что сделано. True — что-то применено."""
    current = version(admin)
    if current is None:
        print(f"{admin.realm}: не проект сервиса входа (нет клиента {' или '.join(PROJECT_CLIENTS)}) — пропущен")
        return False
    if current > len(steps):
        print(f"{admin.realm}: версия {current} новее мигратора ({len(steps)}) — пропущен (развёрнута старая версия?)")
        return False
    if current == len(steps):
        print(f"{admin.realm}: версия {current}, изменений нет")
        return False
    print(f"{admin.realm}: версия {current} → {len(steps)}", flush=True)
    for number, path in steps[current:]:
        summary = load_script(f"realm-migrations/{path.name}").apply(admin)
        # После каждой миграции: прервётся следующая — эта не повторится.
        set_version(admin, number)
        print(f"  {path.stem}: {summary}", flush=True)
    return True


def main():
    parser = argparse.ArgumentParser(description="Миграции настроек проектов (realm) Keycloak")
    parser.add_argument("realms", nargs="*", help="только эти проекты (по умолчанию — все)")
    parser.add_argument("--status", action="store_true", help="показать версии, ничего не меняя")
    args = parser.parse_args()
    steps = migrations()
    targets = args.realms or realms(master_token())

    failed, changed = [], False
    for realm in targets:
        try:
            # Токен — на каждый проект: токен мастер-админки живёт минуту.
            admin = Admin(realm)
            if args.status:
                current = version(admin)
                print(f"{realm}: " + ("не проект сервиса входа" if current is None else f"версия {current} из {len(steps)}"))
            else:
                changed |= migrate(admin, steps)
        except SystemExit as error:
            print(f"{realm}: ошибка — {error}", file=sys.stderr, flush=True)
            failed.append(realm)
    # После проектов: первая строка вывода — всегда первый проект (на неё опираются проверки).
    if not args.realms:
        try:
            if args.status:
                master = Admin("master").call("GET", "")
                print(f"master: защита от перебора пароля {'включена' if master.get('bruteForceProtected') else 'ВЫКЛЮЧЕНА'}")
            else:
                print(harden_master(Admin("master")), flush=True)
        except SystemExit as error:
            print(f"master: ошибка — {error}", file=sys.stderr, flush=True)
            failed.append("master")
    if changed:
        print("Админам обновлённых проектов — войти заново (новые права придут с новым токеном).")
    if failed:
        raise SystemExit(f"Не обновлены: {', '.join(failed)}")


if __name__ == "__main__":
    main()
