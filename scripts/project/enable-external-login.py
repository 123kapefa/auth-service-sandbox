#!/usr/bin/env python3
"""
Вход через Госуслуги и Яндекс ID в проекте (realm): провайдеры с ключами проекта и сценарии входа для них.
Новые проекты получают сценарии из шаблона realm (new-project.py), но не провайдеров — ключи у каждого проекта свои.
Проекту, созданному раньше, и живому стенду песочницы (realm импортируется только в пустую базу) — и сценарии.
  - сценарии (из infra/keycloak/realm/sandbox-realm.json): platform first external login — новая учётная запись или
    привязка к существующей (по телефону или почте) паролем от неё; platform after external login — 2FA, если
    включена, и закрытый доступ к приложению;
  - провайдеры esia и yandex: ключи и адреса — из окружения или .env (как в .env.example: ESIA_*, YANDEX_*);
    адреса не заданы — настоящие (esia.gosuslugi.ru, oauth.yandex.ru); --stub — заглушка песочницы (idp-stub).
Повторный запуск обновляет провайдеров (новые ключи) и не трогает уже созданные сценарии.
  scripts/project/enable-external-login.py sandbox --stub
  ESIA_CLIENT_ID=… ESIA_SIGNER_URL=… ESIA_CERTIFICATE_HASH=… scripts/project/enable-external-login.py shop --esia
"""
import argparse
import json
import os
import urllib.parse

from keycloak_admin import ROOT, Admin

TEMPLATE = ROOT / "infra/keycloak/realm/sandbox-realm.json"
FLOWS = ["platform first external login", "platform after external login"]
STUB_PORT = os.environ.get("IDP_STUB_PORT") or "8190"
# Заглушка песочницы: браузер ходит на localhost, Keycloak — по имени контейнера.
STUB = {
    "ESIA_CLIENT_ID": "SANDBOX",
    "ESIA_AUTHORIZATION_URL": f"http://localhost:{STUB_PORT}/esia/aas/oauth2/v2/ac",
    "ESIA_TOKEN_URL": "http://idp-stub:8190/esia/aas/oauth2/v3/te",
    "ESIA_API_URL": "http://idp-stub:8190/esia",
    "ESIA_SIGNER_URL": "http://idp-stub:8190/sign",
    "ESIA_CERTIFICATE_HASH": "sandbox-certificate-hash",
    "YANDEX_CLIENT_ID": "sandbox-yandex",
    "YANDEX_CLIENT_SECRET": "sandbox-yandex-secret",
    "YANDEX_AUTHORIZATION_URL": f"http://localhost:{STUB_PORT}/yandex/authorize",
    "YANDEX_TOKEN_URL": "http://idp-stub:8190/yandex/token",
    "YANDEX_USER_INFO_URL": "http://idp-stub:8190/yandex/info?format=json",
}
# Настройки провайдера ← переменные; обязательные — без них провайдер не заработает.
PROVIDERS = {
    "esia": {"name": "Госуслуги", "order": "1", "required": ["ESIA_CLIENT_ID", "ESIA_SIGNER_URL", "ESIA_CERTIFICATE_HASH"],
             "config": {"clientId": "ESIA_CLIENT_ID", "authorizationUrl": "ESIA_AUTHORIZATION_URL", "tokenUrl": "ESIA_TOKEN_URL",
                        "apiUrl": "ESIA_API_URL", "signerUrl": "ESIA_SIGNER_URL", "certificateHash": "ESIA_CERTIFICATE_HASH"}},
    "yandex": {"name": "Яндекс ID", "order": "2", "required": ["YANDEX_CLIENT_ID", "YANDEX_CLIENT_SECRET"],
               "config": {"clientId": "YANDEX_CLIENT_ID", "clientSecret": "YANDEX_CLIENT_SECRET",
                          "authorizationUrl": "YANDEX_AUTHORIZATION_URL", "tokenUrl": "YANDEX_TOKEN_URL",
                          "userInfoUrl": "YANDEX_USER_INFO_URL"}},
}


def settings(stub):
    """Переменные ESIA_* и YANDEX_*: окружение важнее .env; --stub — заглушка."""
    values = {}
    env_file = ROOT / ".env"
    if env_file.exists():
        for line in env_file.read_text(encoding="utf-8").splitlines():
            key, sep, value = line.partition("=")
            if sep and not key.startswith("#") and key.strip().startswith(("ESIA_", "YANDEX_")):
                values[key.strip()] = value.split(" #")[0].strip()
    values.update({k: v for k, v in os.environ.items() if k.startswith(("ESIA_", "YANDEX_"))})
    if stub:
        values = dict(STUB)
    return {k: v for k, v in values.items() if v}


def ensure_flows(admin):
    """Сценарии внешнего входа из шаблона — если их ещё нет (подсценарии и настройки шагов — тоже)."""
    template = json.loads(TEMPLATE.read_text(encoding="utf-8"))
    flows = {f["alias"]: f for f in template["authenticationFlows"]}
    configs = {c["alias"]: c["config"] for c in template["authenticatorConfig"]}
    existing = {f["alias"] for f in admin.call("GET", "/authentication/flows")}
    created = []
    for alias in FLOWS:
        if alias in existing:
            continue
        admin.call("POST", "/authentication/flows", {"alias": alias, "description": flows[alias]["description"],
                                                     "providerId": "basic-flow", "topLevel": True, "builtIn": False})
        fill_flow(admin, alias, flows, configs)
        created.append(alias)
    return created


def fill_flow(admin, alias, flows, configs):
    path = "/authentication/flows/" + urllib.parse.quote(alias, safe="") + "/executions"
    for step in sorted(flows[alias]["authenticationExecutions"], key=lambda e: e["priority"]):
        if step["authenticatorFlow"]:
            sub = flows[step["flowAlias"]]
            admin.call("POST", path + "/flow", {"alias": sub["alias"], "description": sub["description"],
                                                "type": "basic-flow", "provider": "registration-page-form"})
        else:
            admin.call("POST", path + "/execution", {"provider": step["authenticator"]})
        added = [e for e in admin.call("GET", path) if e["level"] == 0][-1]
        added["requirement"] = step["requirement"]
        admin.call("PUT", path, added)
        if step.get("authenticatorConfig"):
            admin.call("POST", f"/authentication/executions/{added['id']}/config",
                       {"alias": step["authenticatorConfig"], "config": configs[step["authenticatorConfig"]]})
        if step["authenticatorFlow"]:
            fill_flow(admin, step["flowAlias"], flows, configs)


def ensure_provider(admin, alias, values):
    spec = PROVIDERS[alias]
    missing = [name for name in spec["required"] if name not in values]
    if missing:
        raise SystemExit(f"{spec['name']}: не заданы {', '.join(missing)} (окружение или .env; заглушка — --stub)")
    config = {key: values[name] for key, name in spec["config"].items() if name in values}
    config.update({"syncMode": "IMPORT", "guiOrder": spec["order"]})
    if alias == "yandex":
        config["clientAuthMethod"] = "client_secret_post"
    provider = {"alias": alias, "displayName": spec["name"], "providerId": alias, "enabled": True, "trustEmail": True,
                "storeToken": False, "linkOnly": False, "hideOnLogin": False,
                "firstBrokerLoginFlowAlias": FLOWS[0], "postBrokerLoginFlowAlias": FLOWS[1], "config": config}
    known = {p["alias"] for p in admin.call("GET", "/identity-provider/instances")}
    if alias in known:
        admin.call("PUT", f"/identity-provider/instances/{alias}", provider)
        return "обновлён"
    admin.call("POST", "/identity-provider/instances", provider)
    return "добавлен"


def main():
    parser = argparse.ArgumentParser(description="Вход через Госуслуги и Яндекс ID в проекте")
    parser.add_argument("realm")
    parser.add_argument("--esia", action="store_true", help="только Госуслуги")
    parser.add_argument("--yandex", action="store_true", help="только Яндекс ID")
    parser.add_argument("--stub", action="store_true", help="заглушка песочницы (idp-stub) вместо настоящих ключей")
    args = parser.parse_args()
    aliases = [a for a, on in (("esia", args.esia), ("yandex", args.yandex)) if on] or ["esia", "yandex"]

    admin = Admin(args.realm)
    values = settings(args.stub)
    created = ensure_flows(admin)
    done = [f"{PROVIDERS[a]['name']} — {ensure_provider(admin, a, values)}" for a in aliases]
    print(f"Внешний вход в {args.realm}: " + "; ".join(done) + "."
          + (f" Сценарии созданы: {', '.join(created)}." if created else ""))
    if not args.stub:
        print("Адрес возврата (Redirect URI) для приложения Яндекса и заявки в ЕСИА — публичный адрес Keycloak + "
              f"/realms/{args.realm}/broker/<esia|yandex>/endpoint")


if __name__ == "__main__":
    main()
