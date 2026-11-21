"""
Кабинет Keycloak закрыт (#226): клиенты account и account-console выключены.
Человек работает с учётной записью только в кабинете сервиса входа (/account/): пароль, почта, 2FA, ключи доступа и телефон
меняются через страницы входа с правилами проекта (недавний вход, запись в журнал, письмо, способы входа). Кабинет Keycloak
(/realms/<проект>/account/) и его REST API обходили эти правила: удалить ключ доступа или второй фактор, сменить почту
без подтверждения, оставить аккаунт без способа входа.
Идемпотентна: выключенный клиент не трогается; нет клиента — пропуск. Новые проекты получают клиентов выключенными из шаблона realm.
"""

CLIENTS = ("account", "account-console")


def apply(admin):
    closed, already, missing = [], [], []
    for client_id in CLIENTS:
        client = admin.client(client_id)
        if client is None:
            missing.append(client_id)
        elif client.get("enabled") is False:
            already.append(client_id)
        else:
            admin.call("PUT", f"/clients/{client['id']}", {**client, "enabled": False})
            closed.append(client_id)
    parts = [text + ": " + ", ".join(ids) for text, ids in
             (("закрыт", closed), ("уже был закрыт", already), ("клиента нет", missing)) if ids]
    return "кабинет Keycloak — " + "; ".join(parts)
