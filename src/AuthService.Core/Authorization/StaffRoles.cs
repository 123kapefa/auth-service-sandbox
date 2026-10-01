using PlatformAuth;

namespace AuthService.Core.Authorization;

/// <summary>
/// Роли сотрудника (manager, admin) среди ролей пользователя в приложении. customer есть у всех — роль по умолчанию realm,
/// поэтому «нет ролей сотрудника» и есть покупатель.
/// </summary>
public static class StaffRoles
{
    private static readonly string[] _staff = [AuthModes.MANAGER, AuthModes.ADMIN];

    /// <summary>Роли сотрудника в порядке manager, admin.</summary>
    public static IReadOnlyList<string> Of(IEnumerable<string> clientRoles)
    {
        ArgumentNullException.ThrowIfNull(clientRoles);
        HashSet<string> roles = [.. clientRoles];
        return [.. _staff.Where(roles.Contains)];
    }

    /// <summary>Роль одной строкой, как в боевом сервисе: «manager,admin» или «customer».</summary>
    public static string Describe(IReadOnlyList<string> staffRoles)
    {
        ArgumentNullException.ThrowIfNull(staffRoles);
        return staffRoles.Count == 0 ? AuthModes.CUSTOMER : string.Join(',', staffRoles);
    }
}
