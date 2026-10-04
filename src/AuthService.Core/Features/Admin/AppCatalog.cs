using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Features.Admin;

/// <summary>
/// Роли и права приложения из Keycloak. Приложение — клиент, у которого есть роли (kind=role); у клиента только для
/// входа их нет (одна access-denied) — для него AppNotFound.
/// </summary>
public sealed class AppCatalog(IIdentityAdmin identityAdmin)
{
    public async Task<Result<IReadOnlyList<AppRole>, Error>> LoadAsync(string clientId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AppRole>, Error> roles = await identityAdmin.GetAppRolesAsync(clientId, cancellationToken);
        return roles.IsSuccess && !roles.Value.Any(IsRole)
            ? IdentityProviderErrors.AppNotFound()
            : roles;
    }

    public static bool IsPermission(AppRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return string.Equals(role.Kind, AppRoleKinds.PERMISSION, StringComparison.Ordinal);
    }

    /// <summary>Роль (режим) приложения; служебная access-denied — не роль.</summary>
    public static bool IsRole(AppRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return string.Equals(role.Kind, AppRoleKinds.ROLE, StringComparison.Ordinal)
            && !string.Equals(role.Name, AppRoles.ACCESS_DENIED, StringComparison.Ordinal);
    }
}
