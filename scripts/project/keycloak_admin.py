"""Admin API Keycloak мастер-админкой (токен — scripts/keycloak/master-token.sh): общее для скриптов проекта."""
import json
import os
import subprocess
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
KC = os.environ.get("KC", f"http://localhost:{os.environ.get('KEYCLOAK_PORT') or os.environ.get('AUTH_KEYCLOAK_PORT') or '8180'}")


def master_token():
    token = subprocess.run(
        ["bash", "-c", f"source '{ROOT}/scripts/keycloak/master-token.sh'; master_token"],
        capture_output=True, text=True, check=False).stdout.strip()
    if not token:
        raise SystemExit("Нет токена мастер-админки (KEYCLOAK_ADMIN_PASSWORD в .env)")
    return token


class Admin:
    def __init__(self, realm):
        self.realm = realm
        self.token = master_token()
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
