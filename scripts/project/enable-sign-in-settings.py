#!/usr/bin/env python3
"""
Настройки входа и кодов в админке (этап 10.7) в проекте, созданном раньше, и на живом стенде песочницы (realm
импортируется только в пустую базу). Новые проекты получают всё из шаблона realm (new-project.py).
  - роль realm manage-sign-in и она у служебной учётки сервиса входа (auth-bff; в песочнице sandbox-bff) — плагин
    platform-codes пускает читать и менять настройки кодов только с ней; manage-realm сервису не нужен;
  - право sign-in.manage в клиенте приложения сервиса входа (auth-api; в песочнице sandbox-api) и в роли admin.
Повторный запуск ничего не ломает. Образ Keycloak — с плагином 10.7 (docker compose up -d --build).
  scripts/project/enable-sign-in-settings.py sandbox
"""
import argparse

from keycloak_admin import Admin

ROLE = "manage-sign-in"
PERMISSION = "sign-in.manage"


def first_client(admin, *client_ids):
    for client_id in client_ids:
        client = admin.client(client_id)
        if client:
            return client
    raise SystemExit(f"В realm {admin.realm} нет клиента {' или '.join(client_ids)}")


def enable(admin):
    """Роль manage-sign-in у служебной учётки и право sign-in.manage в роли admin. Возвращает (учётка, клиент API)."""
    admin.call("POST", "/roles", {"name": ROLE, "description": "Служебная: менять настройки входа и кодов проекта (плагин platform-codes) — у учётки сервиса входа"}, ok=(409,))
    role = admin.call("GET", f"/roles/{ROLE}")
    bff = first_client(admin, "auth-bff", "sandbox-bff")
    account = admin.call("GET", f"/clients/{bff['id']}/service-account-user")
    admin.call("POST", f"/users/{account['id']}/role-mappings/realm", [role])

    api = first_client(admin, "auth-api", "sandbox-api")
    admin.call("POST", f"/clients/{api['id']}/roles", {
        "name": PERMISSION, "description": "Вход и коды: каналы, страны, лимиты, бюджет и расход",
        "attributes": {"kind": ["permission"], "source": ["code"]}}, ok=(409,))
    permission = admin.role(api["id"], PERMISSION)
    admin.call("POST", f"/clients/{api['id']}/roles/admin/composites", [permission])
    return account["username"], api["clientId"]


def main():
    parser = argparse.ArgumentParser(description="Настройки входа и кодов (10.7) в проекте, созданном раньше")
    parser.add_argument("realm")
    args = parser.parse_args()
    account, api = enable(Admin(args.realm))
    print(f"Настройки входа в {args.realm}: роль {ROLE} у {account}, право {PERMISSION} в роли admin ({api}).")
    print("Админам проекта — войти заново (новое право придёт с новым токеном).")


if __name__ == "__main__":
    main()
