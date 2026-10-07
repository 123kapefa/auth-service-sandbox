#!/usr/bin/env python3
"""
Этап 10 в проекте, созданном раньше (и на живом стенде песочницы: realm импортируется только в пустую базу).
Новые проекты получают всё это из шаблона realm (new-project.py). Собирает шаги этапа в одну команду:
  - смена номера самим человеком (10.5) — как enable-phone-change.py;
  - предложение ключа доступа после входа по коду (10.6) — как enable-passkey-offer.py;
  - вход и коды в админке (10.7) — роль manage-sign-in и право sign-in.manage, как enable-sign-in-settings.py;
  - капча Яндекс SmartCaptcha на страницах входа (10.8) — её iframe в CSP realm (frame-src).
Повторный запуск ничего не ломает. Образ Keycloak и сервиса — этапа 10 (docker compose up -d --build).
  scripts/project/upgrade-10.py demo-shop
"""
import argparse
import importlib.util
from pathlib import Path

from keycloak_admin import Admin

CAPTCHA = "https://smartcaptcha.yandexcloud.net"
CSP = f"frame-src 'self' {CAPTCHA}; frame-ancestors 'self'; object-src 'none';"


def load(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), Path(__file__).with_name(f"{name}.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


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


def main():
    parser = argparse.ArgumentParser(description="Этап 10 (способы входа и коды) в проекте, созданном раньше")
    parser.add_argument("realm")
    args = parser.parse_args()
    admin = Admin(args.realm)

    phone, offer, settings = load("enable-phone-change"), load("enable-passkey-offer"), load("enable-sign-in-settings")
    phone.enable(admin)
    offer.enable(admin)
    account, api = settings.enable(admin)
    csp = allow_captcha(admin)

    print(f"Этап 10 в {args.realm}: действия {phone.ACTION} и {offer.ACTION} включены, роль {settings.ROLE} у "
          f"{account}, право {settings.PERMISSION} в роли admin ({api}), CSP: {csp}")
    print("Админам проекта — войти заново (новое право придёт с новым токеном).")


if __name__ == "__main__":
    main()
