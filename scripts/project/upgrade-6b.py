#!/usr/bin/env python3
"""
Этап 6б в проекте, созданном раньше (и на живом стенде песочницы: realm импортируется только в пустую базу).
Новые проекты получают всё это из шаблона realm (new-project.py).
  - действия Keycloak, которые кабинет просит у страниц входа: смена почты (UPDATE_EMAIL) — включена и только после
    подтверждения письмом (verifyEmail); 2FA, коды восстановления, ключ доступа, удаление учётных данных — включены;
  - «Вернуться в приложение» на страницах Keycloak (после смены почты) — в личный кабинет: baseUrl клиента входа;
  - оформление проекта (6б.4) — как enable-branding.py.
Повторный запуск ничего не ломает. Образ Keycloak — с плагином и темой 6б (docker compose up -d --build).
  scripts/project/upgrade-6b.py demo-shop
"""
import argparse
import importlib.util
from pathlib import Path

from keycloak_admin import Admin

# Действия при входе (kc_action), которыми пользуется кабинет; у UPDATE_EMAIL — подтверждение новой почты письмом.
ACTIONS = {
    "UPDATE_PASSWORD": {},
    "UPDATE_EMAIL": {"verifyEmail": "true"},
    "CONFIGURE_TOTP": {},
    "CONFIGURE_RECOVERY_AUTHN_CODES": {},
    "webauthn-register-passwordless": {},
    "delete_credential": {},
}


def load_branding():
    spec = importlib.util.spec_from_file_location("enable_branding", Path(__file__).with_name("enable-branding.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def enable_actions(admin):
    unregistered = {a["providerId"]: a for a in admin.call("GET", "/authentication/unregistered-required-actions")}
    for alias, config in ACTIONS.items():
        if alias in unregistered:
            admin.call("POST", "/authentication/register-required-action",
                       {"providerId": alias, "name": unregistered[alias]["name"]})
        action = admin.call("GET", f"/authentication/required-actions/{alias}")
        action["enabled"] = True
        action["config"] = {**(action.get("config") or {}), **config}
        admin.call("PUT", f"/authentication/required-actions/{alias}", action)


def account_return(admin, branding):
    """baseUrl клиента входа — кабинет сервиса этого проекта (адрес сервиса — из redirect URI callback)."""
    bff = branding.first_client(admin, "auth-bff", "sandbox-bff")
    callback = next((u for u in bff.get("redirectUris", []) if u.endswith("/auth/callback")), None)
    if not callback:
        raise SystemExit(f"У {bff['clientId']} нет redirect URI …/auth/callback — адрес сервиса не известен")
    bff["baseUrl"] = callback.removesuffix("/callback") + "/account"
    admin.call("PUT", f"/clients/{bff['id']}", bff)
    return bff["clientId"], bff["baseUrl"]


def main():
    parser = argparse.ArgumentParser(description="Этап 6б (кабинет и оформление) в проекте, созданном раньше")
    parser.add_argument("realm")
    args = parser.parse_args()
    admin = Admin(args.realm)
    branding = load_branding()

    enable_actions(admin)
    client, base_url = account_return(admin, branding)
    account, api = branding.enable(admin)

    print(f"Этап 6б в {args.realm}: действия кабинета включены (смена почты — после подтверждения письмом), "
          f"{client} → {base_url}, роль {branding.ROLE} у {account}, право {branding.PERMISSION} в роли admin ({api}).")
    print("Админам проекта — войти заново (новое право придёт с новым токеном).")


if __name__ == "__main__":
    main()
