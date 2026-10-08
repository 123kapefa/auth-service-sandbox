"""Admin API Keycloak мастер-админкой (токен — scripts/keycloak/master-token.sh): общее для скриптов проекта.
В контейнере (мигратор realm, infra/realm-migrator) docker compose нет: KC_ADMIN_DIRECT=1 — токен прямо у Keycloak
по KC (внутренний адрес), логин KEYCLOAK_ADMIN и пароль KEYCLOAK_ADMIN_PASSWORD из окружения."""
import importlib.util
import json
import os
import subprocess
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
KC = os.environ.get("KC", f"http://localhost:{os.environ.get('KEYCLOAK_PORT') or os.environ.get('AUTH_KEYCLOAK_PORT') or '8180'}")


def load_script(path):
    """Скрипт с дефисом в имени (enable-branding.py) — модулем; путь — от scripts/project."""
    path = Path(__file__).parent / path
    spec = importlib.util.spec_from_file_location(path.stem.replace("-", "_"), path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def master_token():
    if os.environ.get("KC_ADMIN_DIRECT") == "1":
        return direct_token()
    token = subprocess.run(
        ["bash", "-c", f"source '{ROOT}/scripts/keycloak/master-token.sh'; master_token"],
        capture_output=True, text=True, check=False).stdout.strip()
    if not token:
        raise SystemExit("Нет токена мастер-админки (KEYCLOAK_ADMIN_PASSWORD в .env)")
    return token


def direct_token():
    """Токен мастер-админки по паролю (admin-cli). Запрос — изнутри сети Docker: master требует https только снаружи."""
    password = os.environ.get("KEYCLOAK_ADMIN_PASSWORD")
    if not password:
        raise SystemExit("Нет KEYCLOAK_ADMIN_PASSWORD — пароля мастер-админки")
    data = urllib.parse.urlencode({"grant_type": "password", "client_id": "admin-cli",
                                   "username": os.environ.get("KEYCLOAK_ADMIN") or "admin", "password": password}).encode()
    try:
        with urllib.request.urlopen(f"{KC}/realms/master/protocol/openid-connect/token", data) as response:
            return json.load(response)["access_token"]
    except urllib.error.HTTPError as error:
        raise SystemExit(f"Токен мастер-админки: {error.code} {error.read().decode()[:300]}")


def realms(token):
    """Все realm Keycloak (кроме master) — имена."""
    request = urllib.request.Request(f"{KC}/admin/realms?briefRepresentation=true",
                                     headers={"Authorization": f"Bearer {token}"})
    with urllib.request.urlopen(request) as response:
        return [r["realm"] for r in json.load(response) if r["realm"] != "master"]


class Admin:
    def __init__(self, realm, token=None):
        self.realm = realm
        self.token = token or master_token()
        self.base = f"{KC}/admin/realms/{realm}"

    def call(self, method, path, body=None, ok=(200, 201, 204)):
        data = None if body is None else json.dumps(body, ensure_ascii=False).encode()
        request = urllib.request.Request(self.base + path, data=data, method=method, headers={
            "Authorization": f"Bearer {self.token}", "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(request) as response:
                text = response.read().decode()
                return json.loads(text) if text else None
        except urllib.error.HTTPError as error:
            if error.code in ok:
                return None
            raise SystemExit(f"{method} {path}: {error.code} {error.read().decode()[:300]}")

    def client(self, client_id):
        found = self.call("GET", f"/clients?clientId={client_id}")
        return found[0] if found else None

    def role(self, client_uuid, name):
        return self.call("GET", f"/clients/{client_uuid}/roles/{name}")
