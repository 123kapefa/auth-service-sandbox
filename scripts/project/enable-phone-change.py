#!/usr/bin/env python3
"""
Смена номера самим человеком (этап 10.5) в проекте, созданном раньше, и на живом стенде песочницы (realm
импортируется только в пустую базу). Новые проекты получают действие из шаблона realm (new-project.py).
  - действие Keycloak platform-update-phone (плагин code-login): кабинет запускает его, как смену почты.
Повторный запуск ничего не ломает. Образ Keycloak — с плагином 10.5 (docker compose up -d --build).
  scripts/project/enable-phone-change.py sandbox
"""
import argparse

from keycloak_admin import Admin

ACTION = "platform-update-phone"


def enable(admin):
    unregistered = {a["providerId"]: a for a in admin.call("GET", "/authentication/unregistered-required-actions")}
    if ACTION in unregistered:
        admin.call("POST", "/authentication/register-required-action",
                   {"providerId": ACTION, "name": unregistered[ACTION]["name"]})
    action = admin.call("GET", f"/authentication/required-actions/{ACTION}")
    action["enabled"] = True
    admin.call("PUT", f"/authentication/required-actions/{ACTION}", action)


def main():
    parser = argparse.ArgumentParser(description="Смена номера самим человеком (10.5) в проекте, созданном раньше")
    parser.add_argument("realm")
    args = parser.parse_args()
    enable(Admin(args.realm))
    print(f"Смена номера в {args.realm}: действие {ACTION} включено.")


if __name__ == "__main__":
    main()
