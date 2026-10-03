#!/usr/bin/env python3
"""
Подключить приложение к проекту (как «Менеджер 1С» в песочнице):
  - клиент приложения — носитель его ролей и прав и адресат его токенов (bearer-only);
  - роль access-denied — админ закрывает доступ к приложению; --default-role — роль, которая есть у всех (режим
    по умолчанию, например customer), со своими правами — позже в админке;
  - область «{приложение}-contract» — что в токене приложения: role, available_roles, permissions его ролей,
    active_mode (свой режим в приложении — атрибут active_mode.{приложение}), phone, aud;
  - точечные права сервиса входа (FGAP) на клиент приложения — админка управляет его ролями и правами;
  - --web URL — клиент входа приложения «{приложение}-web» (браузер, PKCE) с возвратом на этот адрес.
Каждый шаг проверяет, сделан ли он: после сбоя скрипт можно просто запустить ещё раз.
  scripts/project/add-app.py demo-shop orders "Заказы" --default-role customer --web http://localhost:5300
"""
import argparse
import copy
import json
import re
from urllib.parse import quote
from keycloak_admin import Admin, ROOT

TEMPLATE_APP = "manager-1c"
FGAP_PERMISSION = "Клиенты приложений с ролями: view, map-roles, manage, map-roles-composite"
CODE = r"[a-z][a-z0-9-]*[a-z0-9]"


def contract_scope(app, name):
    """Область токена приложения — по образцу области «Менеджера 1С» в realm песочницы."""
    realm = json.loads((ROOT / "infra/keycloak/realm/sandbox-realm.json").read_text(encoding="utf-8"))
    scope = copy.deepcopy(next(s for s in realm["clientScopes"] if s["name"] == f"{TEMPLATE_APP}-contract"))
    scope = json.loads(json.dumps(scope, ensure_ascii=False).replace(TEMPLATE_APP, app).replace("«Менеджера 1С»", f"«{name}»"))
    scope.pop("id", None)
    for mapper in scope.get("protocolMappers", []):
        mapper.pop("id", None)
    return scope


def ensure_role(admin, client_uuid, role):
    existing = admin.call("GET", f"/clients/{client_uuid}/roles", ok=())
    if not any(r["name"] == role["name"] for r in existing):
        admin.call("POST", f"/clients/{client_uuid}/roles", role, ok=(409,))
    return admin.role(client_uuid, role["name"])


def main():
    parser = argparse.ArgumentParser(description="Подключить приложение к проекту")
    parser.add_argument("realm")
    parser.add_argument("app", help="client_id приложения: строчная латиница, цифры, дефис")
    parser.add_argument("name", help="название приложения")
    parser.add_argument("--default-role", help="роль у всех пользователей проекта (режим по умолчанию)")
    parser.add_argument("--web", help="адрес браузерного приложения: клиент входа {app}-web с возвратом сюда")
    args = parser.parse_args()
    for value, what in ((args.app, "app"), (args.default_role, "--default-role")):
        if value is not None and not re.fullmatch(CODE, value):
            parser.error(f"{what}: строчная латиница, цифры и дефис, с буквы")
    admin = Admin(args.realm)

    if not admin.client(args.app):
        admin.call("POST", "/clients", {"clientId": args.app, "name": args.name, "enabled": True, "bearerOnly": True,
                                        "publicClient": False, "standardFlowEnabled": False, "directAccessGrantsEnabled": False,
                                        "description": f"Приложение «{args.name}»: носитель ролей и audience"}, ok=(409,))
    app = admin.client(args.app)
    ensure_role(admin, app["id"], {"name": "access-denied", "description": f"Доступ к «{args.name}» закрыт"})
    if args.default_role:
        role = ensure_role(admin, app["id"], {"name": args.default_role, "description": args.default_role,
                                              "attributes": {"kind": ["role"], "builtin": ["true"]}})
        default_roles = admin.call("GET", f"/roles/default-roles-{args.realm}")
        composites = admin.call("GET", f"/roles-by-id/{default_roles['id']}/composites")
        if not any(c["id"] == role["id"] for c in composites):
            admin.call("POST", f"/roles-by-id/{default_roles['id']}/composites", [role])

    scope_name = f"{args.app}-contract"
    if not any(s["name"] == scope_name for s in admin.call("GET", "/client-scopes")):
        admin.call("POST", "/client-scopes", contract_scope(args.app, args.name), ok=(409,))

    # Сервис входа (служебная учётка auth-bff) управляет ролями приложения: клиент — в точечное разрешение.
    # Точечные права v2: ресурсы разрешения — сами клиенты (имя ресурса — UUID клиента).
    permissions = admin.client("admin-permissions")
    authz = f"/clients/{permissions['id']}/authz/resource-server"
    found = [p for p in admin.call("GET", f"{authz}/permission?name={quote(FGAP_PERMISSION)}") if p["name"] == FGAP_PERMISSION]
    if not found:
        raise SystemExit(f"В realm {args.realm} нет разрешения «{FGAP_PERMISSION}» — realm создан не шаблоном проекта?")
    permission = found[0]
    resources = [r["name"] for r in admin.call("GET", f"{authz}/policy/{permission['id']}/resources")]
    if app["id"] not in resources:
        policies = [p["id"] for p in admin.call("GET", f"{authz}/policy/{permission['id']}/associatedPolicies")]
        scopes = [s["name"] for s in admin.call("GET", f"{authz}/policy/{permission['id']}/scopes")]
        admin.call("PUT", f"{authz}/permission/scope/{permission['id']}", {
            **{k: permission[k] for k in ("id", "name", "description", "type", "logic", "decisionStrategy") if k in permission},
            "resourceType": "Clients", "resources": resources + [app["id"]], "scopes": scopes, "policies": policies})

    web_client = admin.client(f"{args.app}-web") if args.web else None
    if web_client:
        # Уже есть — адрес приложения мог смениться: возврат, выход и CORS — на новый адрес.
        web = args.web.rstrip("/")
        attributes = {**web_client.get("attributes", {}), "post.logout.redirect.uris": f"{web}/*"}
        admin.call("PUT", f"/clients/{web_client['id']}", {**web_client, "redirectUris": [f"{web}/*"],
                                                           "webOrigins": [web], "attributes": attributes})
    elif args.web:
        web = args.web.rstrip("/")
        admin.call("POST", "/clients", {
            "clientId": f"{args.app}-web", "name": f"{args.name}: вход", "enabled": True, "publicClient": True,
            "standardFlowEnabled": True, "directAccessGrantsEnabled": False, "redirectUris": [f"{web}/*"],
            "webOrigins": [web], "attributes": {"pkce.code.challenge.method": "S256", "platform.app": args.app,
                                               "post.logout.redirect.uris": f"{web}/*"},
            "defaultClientScopes": [scope_name, "openid-min"], "optionalClientScopes": []}, ok=(409,))

    print(f"Приложение «{args.name}» подключено к {args.realm}: клиент {args.app}, область {scope_name}"
          + (f", роль по умолчанию {args.default_role}" if args.default_role else "")
          + (f", вход {args.app}-web ({args.web})" if args.web else ""))


if __name__ == "__main__":
    main()
