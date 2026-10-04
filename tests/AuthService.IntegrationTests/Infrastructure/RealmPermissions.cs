using AuthService.Core.Authorization;
using PlatformAuth;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>
/// Права режима — как их кладёт в токен Keycloak (составные роли клиента sandbox-api в realm JSON).
/// Тестам Keycloak не нужен: токен выпускает фабрика, а эта таблица повторяет состав ролей из realm.
/// </summary>
public static class RealmPermissions
{
    public static string[] Of(string activeMode, IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (!roles.Contains(activeMode))
        {
            return [];
        }

        return activeMode switch
        {
            AuthModes.CUSTOMER => [AuthServicePermissions.Profiles.VIEW],
            AuthModes.MANAGER => [AuthServicePermissions.Profiles.VIEW, AuthServicePermissions.Users.VIEW, AuthServicePermissions.Users.BLOCK],
            AuthModes.ADMIN =>
            [
                AuthServicePermissions.Profiles.VIEW,
                AuthServicePermissions.Users.VIEW,
                AuthServicePermissions.Users.BLOCK,
                AuthServicePermissions.Users.MANAGE_ROLES,
                AuthServicePermissions.Users.MANAGE_SESSIONS,
                AuthServicePermissions.Users.MANAGE_CREDENTIALS,
                AuthServicePermissions.Audit.VIEW,
                AuthServicePermissions.Roles.MANAGE,
                AuthServicePermissions.ProjectServices.MANAGE,
                AuthServicePermissions.Branding.MANAGE,
            ],
            _ => [],
        };
    }
}
