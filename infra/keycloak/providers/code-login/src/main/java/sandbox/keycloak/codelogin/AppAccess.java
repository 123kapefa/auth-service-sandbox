package sandbox.keycloak.codelogin;

import org.keycloak.models.ClientModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;

/**
 * Доступ к приложению: у клиента приложения есть роль access-denied — кому она выдана, тот в это приложение не входит
 * и не обновляет токены. Аккаунт при этом рабочий: в остальные приложения проекта человек входит как обычно.
 */
final class AppAccess {

    /** Роль клиента приложения «доступ закрыт». Есть у клиента — значит, доступом к нему можно управлять. */
    static final String DENIED_ROLE = "access-denied";

    private AppAccess() {
    }

    static boolean isDenied(ClientModel client, UserModel user) {
        if (client == null || user == null) {
            return false;
        }
        RoleModel denied = client.getRole(DENIED_ROLE);
        return denied != null && user.hasRole(denied);
    }
}
