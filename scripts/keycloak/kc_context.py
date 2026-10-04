"""Контекст страницы Keycloak из HTML темы Keycloakify (const kcContext = {...}) — для скриптов проверки.

Страницы рисует React в браузере, в HTML формы нет; всё, что нужно скрипту, — в kcContext: pageId, адрес формы
(url.loginAction), сообщение сервера (message.summary), атрибуты своих страниц. Объект — JavaScript, не JSON:
с комментариями и висячими запятыми — они убираются.

Функции в объекте (messagesPerField.get/existsError) сохраняются строкой с их телом. Ошибки полей Keycloak берёт
из того же message.summary, поэтому сообщение сервера одно — message.summary. Страница — templateName («login.ftl»).

Использование: python3 kc_context.py <html> [выражение над c] — печатает результат (нет контекста или ошибка — пусто).
"""
import json
import re
import sys


def load(path):
    text = open(path, encoding="utf-8").read()
    start = text.find("const kcContext = {")
    if start < 0:
        return None
    i = text.index("{", start)
    depth, in_string, escape = 0, False, False
    for j in range(i, len(text)):
        ch = text[j]
        if in_string:
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == '"':
                in_string = False
        elif ch == '"':
            in_string = True
        elif ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return parse(text[i : j + 1])
    return None


def parse(source):
    out, i, in_string, escape = [], 0, False, False
    while i < len(source):
        ch = source[i]
        if in_string:
            out.append(ch)
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == '"':
                in_string = False
        elif ch == '"':
            in_string = True
            out.append(ch)
        elif source.startswith("/*", i):
            i = source.index("*/", i) + 2
            continue
        elif source.startswith("function", i):
            end = skip_function(source, i)
            out.append(json.dumps(source[i:end]))
            i = end
            continue
        elif source.startswith("undefined", i):
            out.append("null")
            i += len("undefined")
            continue
        else:
            out.append(ch)
        i += 1
    cleaned = re.sub(r",(\s*[}\]])", r"\1", "".join(out))
    return json.loads(cleaned)


def skip_function(source, start):
    """Конец функции JS: до парной закрывающей скобки тела; строки в одинарных кавычках пропускаются."""
    i = source.index("{", start)
    depth, quote = 0, None
    while i < len(source):
        ch = source[i]
        if quote:
            if ch == "\\":
                i += 1
            elif ch == quote:
                quote = None
        elif ch in "'\"":
            quote = ch
        elif ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return len(source)


def dig(value, path):
    for key in path:
        if not isinstance(value, dict) or key not in value:
            return None
        value = value[key]
    return value


if __name__ == "__main__":
    c = load(sys.argv[1])
    if c is None:
        sys.exit(0)
    if len(sys.argv) < 3:
        print(json.dumps(c, ensure_ascii=False, indent=1))
        sys.exit(0)
    try:
        value = eval(sys.argv[2], {"json": json}, {"c": c, "get": lambda *path: dig(c, path)})
    except (KeyError, IndexError, TypeError):
        value = ""
    print("" if value is None else value)
