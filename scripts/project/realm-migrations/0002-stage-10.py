"""
Этап 10: способы входа и коды (бывший upgrade-10.py).
  - смена номера самим человеком (10.5) — как enable-phone-change.py;
  - предложение ключа доступа после входа по коду (10.6) — как enable-passkey-offer.py;
  - вход и коды в админке (10.7) — роль manage-sign-in и право sign-in.manage, как enable-sign-in-settings.py;
  - капча Яндекс SmartCaptcha на страницах входа (10.8) — её iframe в CSP realm (frame-src).
Образ Keycloak и сервиса — этапа 10.
"""
from keycloak_admin import load_script

CAPTCHA = "https://smartcaptcha.yandexcloud.net"
CSP = f"frame-src 'self' {CAPTCHA}; frame-ancestors 'self'; object-src 'none';"


def with_captcha(csp):
    """CSP с капчей во frame-src: уже есть — как есть; нет CSP — как в шаблоне; нет frame-src — добавляем его."""
    if CAPTCHA in csp:
        return csp
    if not csp.strip():
        return CSP
    directives = [d.strip() for d in csp.split(";") if d.strip()]
    for i, directive in enumerate(directives):
        if directive.split()[0] == "frame-src":
            directives[i] = f"{directive} {CAPTCHA}"
            break
    else:
        directives.insert(0, f"frame-src 'self' {CAPTCHA}")
    return "; ".join(directives) + ";"


def allow_captcha(admin):
    headers = admin.call("GET", "").get("browserSecurityHeaders") or {}
    csp = with_captcha(headers.get("contentSecurityPolicy") or "")
    if csp != headers.get("contentSecurityPolicy"):
        admin.call("PUT", "", {"browserSecurityHeaders": {**headers, "contentSecurityPolicy": csp}})
    return csp


def apply(admin):
    phone = load_script("enable-phone-change.py")
    offer = load_script("enable-passkey-offer.py")
    settings = load_script("enable-sign-in-settings.py")
    phone.enable(admin)
    offer.enable(admin)
    account, api = settings.enable(admin)
    csp = allow_captcha(admin)
    return (f"действия {phone.ACTION} и {offer.ACTION} включены, роль {settings.ROLE} у {account}, "
            f"право {settings.PERMISSION} в роли admin ({api}), CSP: {csp}")
