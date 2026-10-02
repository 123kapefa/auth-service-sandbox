using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Features.Admin;

/// <summary>
/// Роли пользователя в приложении. Assigned — выданные напрямую (их меняет админка), Defaults — по умолчанию realm
/// (customer; снять нельзя). Роль = режим: режимы пользователя — все его роли. HasAccess — открыт ли доступ к
/// приложению (null — у приложения нет роли access-denied, закрыть нельзя).
/// </summary>
public sealed record UserRoles(IReadOnlyList<AppRole> Catalog, IReadOnlyList<string> Assigned, IReadOnlyList<string> Defaults, bool? HasAccess = null)
{
    public IReadOnlyList<string> Modes => StaffRoles.Sort(Defaults.Concat(Assigned));

    /// <summary>Роль приложения по имени (регистр не важен) или null.</summary>
    public string? Find(string? name) =>
        Catalog.Where(AppCatalog.IsRole)
            .Select(role => role.Name)
            .FirstOrDefault(role => string.Equals(role, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed class UserAppRoles(IIdentityAdmin identityAdmin, AppCatalog catalog)
{
    public async Task<Result<UserRoles, Error>> LoadAsync(Guid userId, string clientId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AppRole>, Error> roles = await catalog.LoadAsync(clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        Result<IReadOnlyList<string>, Error> direct = await identityAdmin.GetClientRolesAsync(userId, clientId, cancellationToken);
        if (direct.IsFailure)
        {
            return direct.Error;
        }

        Result<IReadOnlyList<string>, Error> effective = await identityAdmin.GetEffectiveClientRolesAsync(userId, clientId, cancellationToken);
        if (effective.IsFailure)
        {
            return effective.Error;
        }

        IReadOnlyList<string> assigned = StaffRoles.Of(direct.Value, roles.Value);
        IReadOnlyList<string> defaults = [.. StaffRoles.Of(effective.Value, roles.Value).Except(assigned, StringComparer.Ordinal)];
        bool? hasAccess = roles.Value.Any(role => string.Equals(role.Name, AppRoles.ACCESS_DENIED, StringComparison.Ordinal))
            ? !direct.Value.Contains(AppRoles.ACCESS_DENIED, StringComparer.Ordinal)
            : null;
        return new UserRoles(roles.Value, assigned, defaults, hasAccess);
    }
}
