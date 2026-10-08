"""
Этап 6б: личный кабинет и оформление проекта (бывший upgrade-6b.py).
  - действия Keycloak, которые кабинет просит у страниц входа: смена почты (UPDATE_EMAIL) — включена и только после
    подтверждения письмом (verifyEmail); 2FA, коды восстановления, ключ доступа, удаление учётных данных — включены;
  - «Вернуться в приложение» на страницах Keycloak (после смены почты) — в личный кабинет: baseUrl клиента входа;
  - оформление проекта (6б.4) — как enable-branding.py.
Образ Keycloak — с плагином и темой 6б.
"""
from keycloak_admin import load_script

# Действия при входе (kc_action), которыми пользуется кабинет; у UPDATE_EMAIL — подтверждение новой почты письмом.
ACTIONS = {
    "UPDATE_PASSWORD": {},
    "UPDATE_EMAIL": {"verifyEmail": "true"},
    "CONFIGURE_TOTP": {},
    "CONFIGURE_RECOVERY_AUTHN_CODES": {},
    "webauthn-register-passwordless": {},
    "delete_credential": {},
}


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


def apply(admin):
    branding = load_script("enable-branding.py")
    enable_actions(admin)
    client, base_url = account_return(admin, branding)
    account, api = branding.enable(admin)
    return (f"действия кабинета включены (смена почты — после подтверждения письмом), {client} → {base_url}, "
            f"роль {branding.ROLE} у {account}, право {branding.PERMISSION} в роли admin ({api})")
