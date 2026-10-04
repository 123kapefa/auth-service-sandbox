using AuthService.Core.Abstractions;
using AuthService.Core.Features.Admin;
using PlatformAuth;

namespace AuthService.Core.Authorization;

/// <summary>
/// Роли сотрудника — роли приложения (kind=role в каталоге), выданные пользователю напрямую. customer есть у всех —
/// роль по умолчанию realm, поэтому «нет ролей сотрудника» и есть покупатель.
/// </summary>
public static class StaffRoles
{
    /// <summary>Роли сотрудника среди ролей клиента: только роли из каталога (не права, не access-denied), по порядку.</summary>
    public static IReadOnlyList<string> Of(IEnumerable<string> clientRoles, IEnumerable<AppRole> catalog)
    {
        ArgumentNullException.ThrowIfNull(clientRoles);
        ArgumentNullException.ThrowIfNull(catalog);
        HashSet<string> roles = [.. catalog.Where(AppCatalog.IsRole).Select(role => role.Name)];
        return Sort(clientRoles.Where(roles.Contains));
    }

    /// <summary>Порядок ролей: встроенные как в боевом сервисе (customer, manager, admin), затем свои по алфавиту.</summary>
    public static IReadOnlyList<string> Sort(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return
        [
            .. roles
                .Distinct(StringComparer.Ordinal)
                .OrderBy(Rank)
                .ThenBy(role => role, StringComparer.Ordinal),
        ];
    }

    private static int Rank(string role)
    {
        for (int index = 0; index < AuthModes.All.Count; index++)
        {
            if (string.Equals(AuthModes.All[index], role, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return AuthModes.All.Count;
    }

    /// <summary>Роль одной строкой, как в боевом сервисе: «manager,admin» или «customer».</summary>
    public static string Describe(IReadOnlyList<string> staffRoles)
    {
        ArgumentNullException.ThrowIfNull(staffRoles);
        return staffRoles.Count == 0 ? AuthModes.CUSTOMER : string.Join(',', staffRoles);
    }
}
