#!/usr/bin/env python3
"""
Realm нового проекта для сервиса входа — из realm демо-проекта (infra/keycloak/realm/sandbox-realm.json), чтобы
настройки входа (сценарии, плагины, тема, роли, права, точечные права сервиса) не расходились с проверенными.

Что делается с realm песочницы:
  - realm и название — новые; тестовых пользователей нет (кроме служебной учётки сервиса);
  - демо-приложения убраны (manager-1c, manager-1c-cli, dev-cli, legacy-import и их области) — приложения проекта
    подключаются scripts/project/add-app.py;
  - клиенты сервиса — auth-api (носитель ролей и прав, адресат токенов) и auth-bff (вход), как во всех проектах;
  - UUID клиентов — новые (UUID клиента уникален на весь Keycloak), точечные права (FGAP) — на новые UUID;
  - адреса сервиса (возврат после входа, выход, back-channel logout) и секрет auth-bff — из параметров;
  - для боя: https обязателен для внешних адресов (sslRequired=external), почта — только если задан --smtp-host;
    --dev — стенд: http, Mailpit, клиент dev-cli.

Запуск:
  scripts/project/new-project.py --realm shop --name "Магазин" --service-url http://localhost:8081 \
      --backchannel-url http://auth-service-shop:8080 --secret "$AUTH_BFF_SECRET" > shop-realm.json
Печатает realm JSON; секрет, если не задан, — случайный (в stderr). Создать в Keycloak — scripts/project/create-project.sh.
"""
import argparse
import copy
import json
import os
import re
import secrets
import sys
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "infra/keycloak/realm/sandbox-realm.json"

DEMO_CLIENTS = {"manager-1c", "manager-1c-cli", "dev-cli", "legacy-import"}
DEMO_SCOPES = {"manager-1c-contract"}
RENAMES = {"sandbox-api": "auth-api", "sandbox-bff": "auth-bff"}


def parse_args():
    parser = argparse.ArgumentParser(description="Realm нового проекта для сервиса входа")
    parser.add_argument("--realm", required=True, help="код проекта = имя realm: строчная латиница, цифры, дефис")
    parser.add_argument("--name", required=True, help="название проекта: на страницах входа и в письмах")
    parser.add_argument("--service-url", required=True, help="внешний адрес сервиса входа проекта (http://localhost:8081)")
    parser.add_argument("--backchannel-url", help="адрес сервиса изнутри сети Keycloak (по умолчанию --service-url)")
    parser.add_argument("--secret", help="секрет клиента auth-bff (по умолчанию — случайный)")
    parser.add_argument("--smtp-host", help="почтовый сервер (без него — письма не отправляются; --dev — mailpit)")
    parser.add_argument("--smtp-port", default="587")
    parser.add_argument("--smtp-from", help="адрес отправителя писем (по умолчанию no-reply@{realm}.test)")
    parser.add_argument("--smtp-user", help="логин почтового сервера (пароль — переменная окружения SMTP_PASSWORD)")
    parser.add_argument("--smtp-no-tls", action="store_true", help="без STARTTLS (только локальный почтовый сервер)")
    parser.add_argument("--dev", action="store_true",
                        help="стенд: http (sslRequired=none), Mailpit, клиент dev-cli (вход по паролю для скриптов) — ТОЛЬКО DEV")
    args = parser.parse_args()
    if not re.fullmatch(r"[a-z][a-z0-9-]*[a-z0-9]", args.realm):
        parser.error("--realm: строчная латиница, цифры и дефис, с буквы")
    if args.smtp_user and (args.dev or args.smtp_no_tls):
        parser.error("--smtp-user без TLS: пароль почты ушёл бы открытым текстом")
    if args.dev and not args.smtp_host:
        args.smtp_host, args.smtp_port = "mailpit", "1025"
    return args


def rename(value):
    """sandbox-api → auth-api, sandbox-bff → auth-bff во всех строках (ключи и значения)."""
    if isinstance(value, dict):
        return {rename(k): rename(v) for k, v in value.items()}
    if isinstance(value, list):
        return [rename(v) for v in value]
    if isinstance(value, str):
        for old, new in RENAMES.items():
            value = value.replace(old, new)
    return value


def main():
    args = parse_args()
    service = args.service_url.rstrip("/")
    backchannel = (args.backchannel_url or service).rstrip("/")
    secret = args.secret or secrets.token_urlsafe(32)
    keep_clients = DEMO_CLIENTS - ({"dev-cli"} if args.dev else set())

    realm = copy.deepcopy(json.loads(SOURCE.read_text(encoding="utf-8")))
    realm["realm"] = args.realm
    realm["displayName"] = args.name
    realm.pop("id", None)
    # https для внешних адресов — в бою обязательно (песочница работает по http).
    realm["sslRequired"] = "none" if args.dev else "external"

    # Демо-приложения и их следы: клиенты, области, роли клиентов, привязки областей.
    removed_ids = {c["id"] for c in realm["clients"] if c["clientId"] in keep_clients and c.get("id")}
    realm["clients"] = [c for c in realm["clients"] if c["clientId"] not in keep_clients]
    realm["clientScopes"] = [s for s in realm["clientScopes"] if s["name"] not in DEMO_SCOPES]
    realm["defaultDefaultClientScopes"] = [s for s in realm["defaultDefaultClientScopes"] if s not in DEMO_SCOPES]
    realm["defaultOptionalClientScopes"] = [s for s in realm.get("defaultOptionalClientScopes", []) if s not in DEMO_SCOPES]
    for client in DEMO_CLIENTS:
        realm["roles"]["client"].pop(client, None)
        realm.get("clientScopeMappings", {}).pop(client, None)

    # Пользователи: только служебная учётка сервиса (её права на realm-management и клиентов приложений).
    realm["users"] = [u for u in realm["users"] if u["username"] == "service-account-sandbox-bff"]

    realm = rename(realm)
    # Имена, в которых Keycloak держит имя realm: роль по умолчанию и адреса личного кабинета.
    text = json.dumps(realm, ensure_ascii=False)
    text = text.replace("default-roles-sandbox", f"default-roles-{args.realm}").replace("/realms/sandbox/", f"/realms/{args.realm}/")
    realm = json.loads(text)

    # Новые UUID клиентов: в одной базе Keycloak UUID клиента уникален на все realm. Ссылки — в точечных правах.
    id_map = {}
    for client in realm["clients"]:
        if client.get("id"):
            id_map[client["id"]] = str(uuid.uuid4())
            client["id"] = id_map[client["id"]]
    # Точечные права: ресурсы — клиенты по UUID; списки ресурсов в настройках разрешений — JSON внутри строки.
    authz = next(c for c in realm["clients"] if c["clientId"] == "admin-permissions")["authorizationSettings"]
    authz["resources"] = [r for r in authz["resources"] if r["name"] not in removed_ids]
    for resource in authz["resources"]:
        resource["name"] = id_map.get(resource["name"], resource["name"])
    for policy in authz.get("policies", []):
        config = policy.get("config") or {}
        if "resources" in config:
            ids = [id_map.get(i, i) for i in json.loads(config["resources"]) if i not in removed_ids]
            config["resources"] = json.dumps(ids)

    for policy in authz.get("policies", []):
        if policy.get("description"):
            policy["description"] = policy["description"].replace("auth-api и manager-1c", "auth-api и приложения проекта")

    # Клиент входа: адреса сервиса этого проекта и секрет.
    bff = next(c for c in realm["clients"] if c["clientId"] == "auth-bff")
    bff["secret"] = secret
    bff["redirectUris"] = [f"{service}/auth/callback"]
    bff["webOrigins"] = ["+"]
    bff["attributes"]["post.logout.redirect.uris"] = f"{service}/*"
    bff["attributes"]["backchannel.logout.url"] = f"{backchannel}/auth/backchannel-logout"

    api = next(c for c in realm["clients"] if c["clientId"] == "auth-api")
    api["name"] = f"{args.name}: API"

    # Почта — только своя: адрес песочницы (Mailpit, без шифрования) в бой не попадает.
    if args.smtp_host:
        smtp = {"host": args.smtp_host, "port": args.smtp_port, "from": args.smtp_from or f"no-reply@{args.realm}.test",
                "fromDisplayName": args.name, "ssl": "false", "starttls": str(not (args.dev or args.smtp_no_tls)).lower(), "auth": "false"}
        if args.smtp_user:
            smtp.update(auth="true", user=args.smtp_user, password=os.environ.get("SMTP_PASSWORD", ""))
        realm["smtpServer"] = smtp
    else:
        realm.pop("smtpServer", None)
        print("Внимание: почта не задана (--smtp-host) — письма (сброс пароля, код на почту) не отправятся", file=sys.stderr)

    leftovers = sorted(set(re.findall(r"[\w.-]*sandbox[\w.-]*", json.dumps(realm, ensure_ascii=False), re.IGNORECASE)))
    if leftovers:
        print(f"Внимание: в realm остались упоминания песочницы: {leftovers}", file=sys.stderr)
    if not args.secret:
        print(f"Секрет auth-bff (сохраните в Keycloak__ClientSecret сервиса проекта): {secret}", file=sys.stderr)
    json.dump(realm, sys.stdout, ensure_ascii=False, indent=2)
    print()


if __name__ == "__main__":
    main()
