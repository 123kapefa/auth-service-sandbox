"""Заглушка Яндекс ID, Госуслуг (ЕСИА) и телефонии для песочницы — чтобы проверить вход без настоящих ключей.

Ведёт себя, как настоящие сервисы, в той части, которую видит Keycloak:
  Яндекс ID   GET  /yandex/authorize   — страница «войти как» (тестовые люди) → redirect_uri?code&state
              POST /yandex/token        — код → access_token (client_id и client_secret в теле)
              GET  /yandex/info         — профиль, заголовок «Authorization: OAuth <токен>»
  Госуслуги   GET  /esia/aas/oauth2/v2/ac   — проверяет подпись client_secret, state (UUID), timestamp, хэш сертификата
              POST /esia/aas/oauth2/v3/te   — то же для обмена кода; access_token — JWT с urn:esia:sbj_id
              GET  /esia/rs/prns/{oid}      — человек; /esia/rs/prns/{oid}/ctts?embed=(elements) — контакты
  Подпись     POST /sign                — «подпись ГОСТ» текста: здесь HMAC-SHA256 с ключом заглушки, base64
  Телефония   POST /phone/call          — звонок-сброс (phone=79990000001) → {"code": последние 4 цифры, "from": номер};
                                          номера 7995… «недоступны» (503) — так проверяется запасная SMS
              POST /phone/sms           — SMS (phone, code, text)
              GET  /phone               — страница «звонки и SMS» (последние сверху);
              GET  /phone/messages?to=79990000001 — то же в JSON для скриптов проверки (последние в конце)
Только stdlib Python: образ python:3.13-alpine без сборки (compose.yaml → idp-stub).
"""
import base64
import hashlib
import hmac
import html
import json
import os
import re
import secrets
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlencode, urlparse

PORT = 8190
SIGN_KEY = b"idp-stub-esia-key"
YANDEX_CLIENT_ID = os.environ.get("YANDEX_CLIENT_ID", "sandbox-yandex")
YANDEX_CLIENT_SECRET = os.environ.get("YANDEX_CLIENT_SECRET", "sandbox-yandex-secret")
ESIA_CLIENT_ID = os.environ.get("ESIA_CLIENT_ID", "SANDBOX")
ESIA_CERTIFICATE_HASH = os.environ.get("ESIA_CERTIFICATE_HASH", "sandbox-certificate-hash")
ESIA_TIMESTAMP = re.compile(r"^\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2} [+-]\d{4}$")

# Тестовые люди. Телефоны 7999000010x — новые; 79990000001 — уже есть в песочнице (customer) — вход предложит привязку.
YANDEX_PEOPLE = {
    "new": {"id": "1130000001", "login": "anna.yandex", "first_name": "Анна", "last_name": "Яндексова",
            "default_email": "anna.yandex@sandbox.test", "emails": ["anna.yandex@sandbox.test"],
            "default_phone": {"id": 1, "number": "+79990000101"}},
    "existing": {"id": "1130000002", "login": "customer.yandex", "first_name": "Покупатель", "last_name": "Яндексов",
                 "default_email": "customer.yandex@sandbox.test", "emails": ["customer.yandex@sandbox.test"],
                 "default_phone": {"id": 2, "number": "+79990000001"}},
    # Телефон пользователя с 2FA (79990000006): привязка — пароль, затем код из приложения-аутентификатора.
    "otp": {"id": "1130000004", "login": "otp.yandex", "first_name": "С", "last_name": "Двухфакторной",
            "default_email": "otp.yandex@sandbox.test", "emails": ["otp.yandex@sandbox.test"],
            "default_phone": {"id": 4, "number": "+79990000006"}},
    "nophone": {"id": "1130000003", "login": "nophone.yandex", "first_name": "Без", "last_name": "Телефона",
                "default_email": "nophone.yandex@sandbox.test", "emails": ["nophone.yandex@sandbox.test"]},
}
ESIA_PEOPLE = {
    "new": {"oid": "1000000101", "person": {"firstName": "Мария", "lastName": "Госуслугова", "middleName": "Ивановна", "trusted": True},
            "contacts": [{"type": "MBT", "value": "+7(999)0000102", "vrfStu": "VERIFIED"},
                         {"type": "EML", "value": "maria.esia@sandbox.test", "vrfStu": "VERIFIED"}]},
    # Номер существующего покупателя, но не подтверждён в ЕСИА: привязки по нему быть не должно.
    "unverified": {"oid": "1000000102", "person": {"firstName": "Пётр", "lastName": "Неподтверждённый", "trusted": False},
                   "contacts": [{"type": "MBT", "value": "+7(999)0000001", "vrfStu": "NOT_VERIFIED"}]},
}

PHONE_UNREACHABLE = "7995"   # звонки на такие номера «не проходят»
PHONE_KEEP = 500             # сколько звонков и SMS помнить

codes = {}    # код → (провайдер, человек, client_id, redirect_uri)
phone_log = []  # звонки и SMS: {"kind", "to", "from", "code", "text", "at"}
tokens = {}   # access_token → (провайдер, человек)


def sign(text):
    return base64.b64encode(hmac.new(SIGN_KEY, text.encode(), hashlib.sha256).digest()).decode()


def sign_ok(signature, text):
    expected = base64.urlsafe_b64encode(hmac.new(SIGN_KEY, text.encode(), hashlib.sha256).digest()).decode().rstrip("=")
    return hmac.compare_digest(signature or "", expected)


def is_uuid(value):
    try:
        return str(uuid.UUID(value)) == value
    except (ValueError, TypeError):
        return False


def jwt(payload):
    part = lambda obj: base64.urlsafe_b64encode(json.dumps(obj).encode()).decode().rstrip("=")
    return part({"alg": "GOST3410_2012_256", "typ": "JWT"}) + "." + part(payload) + ".stub-signature"


def people_page(title, people, approve, query, labels):
    """Страница «войти как»: ссылки на approve (по ним и ходят скрипты проверки: a.person[data-key])."""
    links = "".join(
        f'<li><a class="person" data-key="{key}" href="{approve}?{urlencode({**query, "person": key})}">{html.escape(labels(p))}</a></li>'
        for key, p in people.items())
    deny = f'{approve}?{urlencode({**query, "person": "", "deny": "1"})}'
    return f"""<!doctype html><html lang="ru"><meta charset="utf-8"><title>{title} — заглушка</title>
<body style="font-family:system-ui;max-width:32rem;margin:3rem auto"><h1>{title}</h1>
<p>Заглушка песочницы: настоящего входа нет, выберите тестового человека.</p><ul>{links}</ul>
<p><a class="deny" href="{deny}">Отказаться</a></p></body></html>"""


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        print("[idp-stub] " + fmt % args, flush=True)

    def send(self, status, body, content_type="application/json; charset=utf-8"):
        data = body.encode() if isinstance(body, str) else json.dumps(body, ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def redirect(self, url):
        self.send_response(302)
        self.send_header("Location", url)
        self.end_headers()

    def form(self):
        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length).decode()
        return raw, {k: v[0] for k, v in parse_qs(raw, keep_blank_values=True).items()}

    def bearer(self, scheme):
        value = self.headers.get("Authorization", "")
        return tokens.get(value[len(scheme) + 1:]) if value.startswith(scheme + " ") else None

    # --- GET ---------------------------------------------------------------------------------------------------------
    def do_GET(self):
        url = urlparse(self.path)
        q = {k: v[0] for k, v in parse_qs(url.query, keep_blank_values=True).items()}
        path = url.path
        if path == "/health":
            return self.send(200, {"status": "UP"})
        if path == "/phone/messages":
            to = q.get("to", "")
            return self.send(200, [m for m in phone_log if not to or m["to"] == to])
        if path == "/phone":
            rows = "".join(
                f'<tr><td>{time.strftime("%H:%M:%S", time.localtime(m["at"]))}</td><td>{"звонок" if m["kind"] == "call" else "SMS"}</td>'
                f'<td>+{html.escape(m["to"])}</td><td>{html.escape(m.get("from") or m.get("text") or "")}</td></tr>'
                for m in reversed(phone_log))
            return self.send(200, f"""<!doctype html><html lang="ru"><meta charset="utf-8"><title>Телефония — заглушка</title>
<body style="font-family:system-ui;max-width:48rem;margin:3rem auto"><h1>Звонки и SMS</h1>
<p>Заглушка песочницы: настоящих звонков и SMS нет. Код звонка — последние 4 цифры номера «кто звонит».</p>
<table cellpadding="6"><tr><th>Когда</th><th>Что</th><th>Кому</th><th>Кто звонит / текст</th></tr>{rows}</table></body></html>""",
                             "text/html; charset=utf-8")
        if path == "/yandex/authorize":
            if q.get("client_id") != YANDEX_CLIENT_ID or q.get("response_type") != "code" or not q.get("redirect_uri"):
                return self.send(400, {"error": "invalid_request", "error_description": "client_id, response_type=code, redirect_uri"})
            keep = {k: q[k] for k in ("client_id", "redirect_uri", "state") if k in q}
            return self.send(200, people_page("Яндекс ID", YANDEX_PEOPLE, "/yandex/approve", keep,
                                              lambda p: f'{p["first_name"]} {p["last_name"]} ({p["login"]})'), "text/html; charset=utf-8")
        if path == "/yandex/approve":
            return self.approve("yandex", YANDEX_PEOPLE, q)
        if path == "/yandex/info":
            found = self.bearer("OAuth")
            if not found or found[0] != "yandex":
                return self.send(401, {"error": "unauthorized", "error_description": "нужен заголовок Authorization: OAuth <токен>"})
            return self.send(200, {**YANDEX_PEOPLE[found[1]], "client_id": YANDEX_CLIENT_ID, "psuid": "stub"})
        if path == "/esia/aas/oauth2/v2/ac":
            problem = self.esia_check(q, ("client_id", "scope", "timestamp", "state", "redirect_uri"))
            if problem:
                return self.send(400, f"<h1>ЕСИА: ошибка запроса</h1><p>{html.escape(problem)}</p>", "text/html; charset=utf-8")
            keep = {k: q[k] for k in ("client_id", "redirect_uri", "state")}
            return self.send(200, people_page("Госуслуги", ESIA_PEOPLE, "/esia/approve", keep,
                                              lambda p: f'{p["person"]["firstName"]} {p["person"]["lastName"]}'), "text/html; charset=utf-8")
        if path == "/esia/approve":
            return self.approve("esia", ESIA_PEOPLE, q)
        match = re.fullmatch(r"/esia/rs/prns/(\d+)(/ctts)?", path)
        if match:
            found = self.bearer("Bearer")
            person = ESIA_PEOPLE.get(found[1]) if found and found[0] == "esia" else None
            if not person or person["oid"] != match.group(1):
                return self.send(401, {"code": "ESIA-005011", "message": "нет доступа"})
            if match.group(2):
                if q.get("embed") != "(elements)":
                    return self.send(400, {"message": "ожидали embed=(elements)"})
                return self.send(200, {"stateFacts": ["hasSize"], "size": len(person["contacts"]), "elements": person["contacts"]})
            return self.send(200, person["person"])
        self.send(404, {"error": "not_found"})

    def approve(self, provider, people, q):
        redirect_uri, state = q.get("redirect_uri", ""), q.get("state", "")
        sep = "&" if "?" in redirect_uri else "?"
        if q.get("deny"):
            return self.redirect(f"{redirect_uri}{sep}{urlencode({'error': 'access_denied', 'state': state})}")
        if q.get("person") not in people:
            return self.send(400, {"error": "unknown person"})
        code = secrets.token_urlsafe(16)
        codes[code] = (provider, q["person"], q.get("client_id"), redirect_uri)
        self.redirect(f"{redirect_uri}{sep}{urlencode({'code': code, 'state': state})}")

    def esia_check(self, q, signed_fields, code=""):
        """Что не так с запросом к ЕСИА (None — всё верно): клиент, state, timestamp, хэш сертификата, подпись."""
        if q.get("client_id") != ESIA_CLIENT_ID:
            return "неизвестный client_id"
        if not is_uuid(q.get("state")):
            return "state — не UUID"
        if not ESIA_TIMESTAMP.match(q.get("timestamp", "")):
            return "timestamp — не «yyyy.MM.dd HH:mm:ss Z»"
        if q.get("client_certificate_hash") != ESIA_CERTIFICATE_HASH:
            return "неверный client_certificate_hash"
        if not sign_ok(q.get("client_secret"), "".join(q.get(f, "") for f in signed_fields) + code):
            return "подпись client_secret не сходится"
        return None

    # --- POST --------------------------------------------------------------------------------------------------------
    def do_POST(self):
        path = urlparse(self.path).path
        raw, f = self.form()
        if path == "/sign":
            return self.send(200, sign(raw), "text/plain; charset=utf-8")
        if path == "/phone/call":
            phone = f.get("phone", "")
            if not phone.isdigit():
                return self.send(400, {"error": "phone — цифры номера"})
            if phone.startswith(PHONE_UNREACHABLE):
                return self.send(503, {"error": "звонок не прошёл"})
            code = f"{secrets.randbelow(10000):04d}"
            caller = f"+7900{secrets.randbelow(1000):03d}{code}"
            self.remember({"kind": "call", "to": phone, "from": caller, "code": code})
            return self.send(200, {"code": code, "from": caller})
        if path == "/phone/sms":
            if not f.get("phone", "").isdigit() or not f.get("text"):
                return self.send(400, {"error": "нужны phone и text"})
            self.remember({"kind": "sms", "to": f["phone"], "code": f.get("code", ""), "text": f["text"]})
            return self.send(200, {"ok": True})
        if path == "/yandex/token":
            if f.get("client_id") != YANDEX_CLIENT_ID or f.get("client_secret") != YANDEX_CLIENT_SECRET:
                return self.send(401, {"error": "invalid_client"})
            return self.issue("yandex", f, lambda person: {"access_token": self.new_token("yandex", person), "token_type": "bearer", "expires_in": 3600})
        if path == "/esia/aas/oauth2/v3/te":
            if f.get("grant_type") != "authorization_code" or f.get("token_type") != "Bearer":
                return self.send(400, {"error": "invalid_request"})
            problem = self.esia_check(f, ("client_id", "scope", "timestamp", "state", "redirect_uri"), f.get("code", ""))
            if problem:
                return self.send(400, {"error": "invalid_request", "error_description": problem})
            return self.issue("esia", f, lambda person: self.esia_tokens(person, f["state"]))
        self.send(404, {"error": "not_found"})

    @staticmethod
    def remember(message):
        phone_log.append({**message, "at": time.time()})
        del phone_log[:-PHONE_KEEP]
        print(f'[idp-stub] {message["kind"]} → +{message["to"]}: {message.get("from") or message.get("text")}', flush=True)

    def issue(self, provider, f, body):
        found = codes.pop(f.get("code", ""), None)
        if not found or found[0] != provider or found[2] != f.get("client_id") or found[3] != f.get("redirect_uri"):
            return self.send(400, {"error": "invalid_grant"})
        self.send(200, body(found[1]))

    def new_token(self, provider, person):
        token = secrets.token_urlsafe(24)
        tokens[token] = (provider, person)
        return token

    def esia_tokens(self, person, state):
        oid = ESIA_PEOPLE[person]["oid"]
        access = jwt({"urn:esia:sbj_id": int(oid), "scope": "openid fullname email mobile", "client_id": ESIA_CLIENT_ID})
        tokens[access] = ("esia", person)
        return {"access_token": access, "id_token": jwt({"sub": oid, "urn:esia:sbj": {"urn:esia:sbj:oid": oid}}),
                "state": state, "token_type": "Bearer", "expires_in": 3600}


if __name__ == "__main__":
    print(f"[idp-stub] Яндекс ID, Госуслуги и телефония (заглушка) на :{PORT}", flush=True)
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
