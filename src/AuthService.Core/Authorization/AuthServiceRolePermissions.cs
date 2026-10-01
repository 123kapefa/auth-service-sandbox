using System.Collections.Frozen;
using PlatformAuth;

namespace AuthService.Core.Authorization;

/// <summary>Карта «роль → права» сервиса. Права выдаются по активному режиму пользователя.</summary>
public sealed class AuthServiceRolePermissions : IRolePermissionMap
{
    private static readonly FrozenDictionary<string, string[]> _permissionsByRole =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [AuthModes.CUSTOMER] =
            [
                AuthServicePermissions.Profiles.VIEW,
            ],
            [AuthModes.MANAGER] =
            [
                AuthServicePermissions.Profiles.VIEW,
                AuthServicePermissions.Users.VIEW,
                AuthServicePermissions.Users.BLOCK,
            ],
            [AuthModes.ADMIN] =
            [
                AuthServicePermissions.Profiles.VIEW,
                AuthServicePermissions.Users.VIEW,
                AuthServicePermissions.Users.BLOCK,
                AuthServicePermissions.Users.MANAGE_ROLES,
                AuthServicePermissions.Users.MANAGE_SESSIONS,
                AuthServicePermissions.Audit.VIEW,
            ],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public IReadOnlyCollection<string> GetPermissions(string role) =>
        _permissionsByRole.TryGetValue(role, out string[]? permissions) ? permissions : [];
}
