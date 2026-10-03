package sandbox.keycloak.codelogin;

import java.util.HashSet;
import java.util.List;
import java.util.Set;

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
    static final String VERSION = "version";

    private AppRoles() {
    }

    static boolean isPermission(RoleModel role) {
        return PERMISSION.equals(role.getFirstAttribute(KIND));
    }

    /**
     * Права активного режима: режим — роль приложения, которая есть у пользователя; права — права в её составе
     * (с вложенными составными ролями). Режима нет или такой роли у пользователя нет — прав нет.
     */
    static List<String> permissionsOf(ClientModel client, UserModel user, String activeMode) {
        RoleModel mode = activeMode == null ? null : client.getRole(activeMode);
        if (mode == null || isPermission(mode) || !user.hasRole(mode)) {
            return List.of();
        }

        Set<RoleModel> composites = new HashSet<>();
        collect(mode, composites);
        return composites.stream()
            .filter(AppRoles::isPermission)
            .map(RoleModel::getName)
            .sorted()
            .toList();
    }

    /**
     * Права служебной учётки (сервис проекта с ключом доступа): у сервиса нет режимов — права только выданные ему прямо.
     * Роли по умолчанию realm (customer и её права) учётка получает при создании — сервису они не нужны и не считаются.
     */
    static List<String> servicePermissionsOf(ClientModel client, UserModel serviceAccount) {
        return serviceAccount.getClientRoleMappingsStream(client)
            .filter(AppRoles::isPermission)
            .map(RoleModel::getName)
            .sorted()
            .toList();
    }

    /** Версия роли активного режима (атрибут version), если режим — роль пользователя; иначе null. */
    static String versionOf(ClientModel client, UserModel user, String activeMode) {
        RoleModel mode = activeMode == null ? null : client.getRole(activeMode);
        return mode == null || isPermission(mode) || !user.hasRole(mode) ? null : mode.getFirstAttribute(VERSION);
    }

    private static void collect(RoleModel role, Set<RoleModel> into) {
        role.getCompositesStream().forEach(child -> {
            if (into.add(child)) {
                collect(child, into); // защита от циклов: уже собранную роль не обходим повторно
            }
        });
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
