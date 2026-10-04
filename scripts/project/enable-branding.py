#!/usr/bin/env python3
"""
Оформление проекта (этап 6б.4) в проекте, созданном раньше: в новых проектах всё это уже есть из шаблона realm.
  - роль realm manage-branding и она у служебной учётки сервиса входа (auth-bff; в песочнице sandbox-bff) — плагин
    platform-branding пускает менять оформление только с ней; manage-realm сервису не нужен;
  - право branding.manage в клиенте приложения сервиса входа (auth-api; в песочнице sandbox-api) и в роли admin.
Повторный запуск ничего не ломает. Образ Keycloak — с плагином (пересобран после 6б.4).
  scripts/project/enable-branding.py demo-shop
"""
import argparse
from keycloak_admin import Admin

ROLE = "manage-branding"
PERMISSION = "branding.manage"


def first_client(admin, *client_ids):
    for client_id in client_ids:
        client = admin.client(client_id)
        if client:
            return client
    raise SystemExit(f"В realm {admin.realm} нет клиента {' или '.join(client_ids)}")


def enable(admin):
    """Роль manage-branding у служебной учётки и право branding.manage в роли admin. Возвращает (учётка, клиент API)."""
    admin.call("POST", "/roles", {"name": ROLE, "description": "Служебная: менять оформление проекта (плагин platform-branding) — у учётки сервиса входа"}, ok=(409,))
    role = admin.call("GET", f"/roles/{ROLE}")
    bff = first_client(admin, "auth-bff", "sandbox-bff")
    account = admin.call("GET", f"/clients/{bff['id']}/service-account-user")
    admin.call("POST", f"/users/{account['id']}/role-mappings/realm", [role])

    api = first_client(admin, "auth-api", "sandbox-api")
    admin.call("POST", f"/clients/{api['id']}/roles", {
        "name": PERMISSION, "description": "Оформление проекта: название, логотип, цвет, тема",
        "attributes": {"kind": ["permission"], "source": ["code"]}}, ok=(409,))
    permission = admin.role(api["id"], PERMISSION)
    admin.call("POST", f"/clients/{api['id']}/roles/admin/composites", [permission])
    return account["username"], api["clientId"]


def main():
    parser = argparse.ArgumentParser(description="Оформление проекта в проекте, созданном до 6б.4")
    parser.add_argument("realm")
    args = parser.parse_args()
    account, api = enable(Admin(args.realm))
    print(f"Оформление в {args.realm}: роль {ROLE} у {account}, право {PERMISSION} в роли admin ({api}).")
    print("Админам проекта — войти заново (новое право придёт с новым токеном).")

if __name__ == "__main__":
    main()
