package sandbox.keycloak.codelogin;

import java.util.List;

import org.keycloak.models.ClientModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;

/**
 * Роли и права приложения — роли его клиента. Право помечено атрибутом kind=permission, остальные роли клиента —
 * роли (они же режимы). Роль составная: в неё входят права. Так карта «роль → права» живёт в Keycloak, а не в коде.
 */
final class AppRoles {

    static final String KIND = "kind";
    static final String PERMISSION = "permission";

    private AppRoles() {
    }

    static boolean isPermission(RoleModel role) {
        return PERMISSION.equals(role.getFirstAttribute(KIND));
    }

    /** Роли пользователя в приложении (с учётом ролей по умолчанию и групп), без прав, по алфавиту. */
    static List<String> rolesOf(ClientModel client, UserModel user) {
        return client.getRolesStream()
            .filter(role -> !isPermission(role))
            .filter(user::hasRole)
            .map(RoleModel::getName)
            .sorted()
            .toList();
    }
}
