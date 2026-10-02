package sandbox.keycloak.codelogin;

import org.keycloak.models.ClientModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;

/**
 * Доступ к приложению: у клиента приложения есть роль access-denied — кому она выдана, тот в это приложение не входит
 * и не обновляет токены. Аккаунт при этом рабочий: в остальные приложения проекта человек входит как обычно.
 * Клиент входа может принадлежать приложению с ролями на другом клиенте (у «Менеджера 1С» роли — на manager-1c):
 * атрибут клиента sandbox.app указывает, у какого клиента смотреть access-denied.
 */
final class AppAccess {

    /** Роль клиента приложения «доступ закрыт». Есть у клиента — значит, доступом к нему можно управлять. */
    static final String DENIED_ROLE = "access-denied";

    /** Атрибут клиента входа: clientId приложения, к которому он относится. */
    static final String APP_ATTRIBUTE = "sandbox.app";

    private AppAccess() {
    }

    static boolean isDenied(ClientModel client, UserModel user) {
        if (client == null || user == null) {
            return false;
        }
        ClientModel app = appOf(client);
        RoleModel denied = app.getRole(DENIED_ROLE);
        return denied != null && user.hasRole(denied);
    }

    /** Приложение клиента: по атрибуту sandbox.app, иначе — сам клиент. */
    static ClientModel appOf(ClientModel client) {
        String appId = client.getAttribute(APP_ATTRIBUTE);
        if (appId == null || appId.isBlank()) {
            return client;
        }
        ClientModel app = client.getRealm().getClientByClientId(appId);
        return app != null ? app : client;
    }
}
