#!/usr/bin/env python3
"""
Первый админ проекта: пользователь с ролью admin в auth-api и режимом admin — без него в новом проекте некому войти
в админку и выдать роли остальным. Пароль не задаётся: вход по коду (в песочнице код — в логе Keycloak) или пароль
задать потом в «Настройках входа». --password — ТОЛЬКО DEV.
  scripts/project/add-admin.py demo-shop 79991234567 [--email admin@demo-shop.test] [--password Test1234!]
"""
import argparse
import re
from keycloak_admin import Admin


def main():
    parser = argparse.ArgumentParser(description="Первый админ проекта")
    parser.add_argument("realm")
    parser.add_argument("phone", help="телефон: 79991234567 или +7 999 123-45-67")
    parser.add_argument("--email")
    parser.add_argument("--password", help="ТОЛЬКО DEV: постоянный пароль")
    args = parser.parse_args()

    digits = re.sub(r"\D", "", args.phone)
    if len(digits) == 11 and digits[0] == "8":
        digits = "7" + digits[1:]
    elif len(digits) == 10:
        digits = "7" + digits
    admin = Admin(args.realm)

    user = {"username": digits, "enabled": True, "attributes": {"phone": [f"+{digits}"], "active_mode": ["admin"]}}
    if args.email:
        user.update(email=args.email, emailVerified=True)
    if args.password:
        user["credentials"] = [{"type": "password", "value": args.password, "temporary": False}]
    admin.call("POST", "/users", user, ok=(409,))
    user_id = admin.call("GET", f"/users?username={digits}&exact=true")[0]["id"]
    admin.call("PUT", f"/users/{user_id}", {"attributes": user["attributes"], **({"email": args.email, "emailVerified": True} if args.email else {})})

    api = admin.client("auth-api")
    admin.call("POST", f"/users/{user_id}/role-mappings/clients/{api['id']}", [admin.role(api["id"], "admin")])
    print(f"Админ проекта {args.realm}: +{digits} (id {user_id}) — роль admin в auth-api, режим admin")


if __name__ == "__main__":
    main()
