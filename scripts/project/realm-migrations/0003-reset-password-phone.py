"""
«Забыли пароль?»: номер приводится к логину на сервере (#57). Сценарий сброса realm — копия встроенного
«reset credentials», в которой шаг выбора пользователя заменён на platform-reset-choose-user (плагин code-login).
Образ Keycloak — с этим шагом. Новые проекты получают сценарий из шаблона realm.
"""
from urllib.parse import quote

FLOW = "platform reset credentials"
BUILT_IN = "reset credentials"
OLD = "reset-credentials-choose-user"
NEW = "platform-reset-choose-user"


def executions(admin):
    return admin.call("GET", f"/authentication/flows/{quote(FLOW)}/executions")


def first_level(admin):
    return [e for e in executions(admin) if e.get("level") == 0]


def apply(admin):
    """По реальному состоянию сценария, а не по метке: оборванный прогон при повторе достраивается."""
    flows = {f["alias"] for f in admin.call("GET", "/authentication/flows")}
    if FLOW not in flows:
        admin.call("POST", f"/authentication/flows/{quote(BUILT_IN)}/copy", {"newName": FLOW})

    items = executions(admin)
    if not any(e.get("providerId") == NEW for e in items):
        old = next((e for e in items if e.get("providerId") == OLD), None)
        if old is not None:
            admin.call("DELETE", f"/authentication/executions/{old['id']}")
        admin.call("POST", f"/authentication/flows/{quote(FLOW)}/executions/execution", {"provider": NEW})

    new = next(e for e in executions(admin) if e.get("providerId") == NEW)
    if new.get("requirement") != "REQUIRED":
        new["requirement"] = "REQUIRED"
        admin.call("PUT", f"/authentication/flows/{quote(FLOW)}/executions", new)
    # Шаг выбора человека — первый на верхнем уровне: поднимаем, пока не станет.
    for _ in range(len(first_level(admin))):
        if first_level(admin)[0].get("providerId") == NEW:
            break
        admin.call("POST", f"/authentication/executions/{new['id']}/raise-priority")

    if admin.call("GET", "").get("resetCredentialsFlow") != FLOW:
        admin.call("PUT", "", {"resetCredentialsFlow": FLOW})
    return f"сценарий «{FLOW}» включён (шаг {NEW})"
