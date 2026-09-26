using System.Collections.Frozen;
using System.Security.Claims;

namespace PlatformAuth;

public interface IPermissionResolver
{
    /// <summary>Права пользователя в его активном режиме.</summary>
    IReadOnlySet<string> GetPermissions(ClaimsPrincipal user);
}

/// <summary>
/// Права считаются по активному режиму, а не по всем ролям сразу: админ в режиме покупателя
/// получает права покупателя. Режим без соответствующей роли не даёт прав.
/// </summary>
internal sealed class ActiveModePermissionResolver(IRolePermissionMap map) : IPermissionResolver
{
    public IReadOnlySet<string> GetPermissions(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        string? activeMode = user.FindFirstValue(AuthClaims.ACTIVE_MODE);
        if (activeMode is null || !user.IsInRole(activeMode))
        {
            return FrozenSet<string>.Empty;
        }

        return map.GetPermissions(activeMode).ToFrozenSet(StringComparer.Ordinal);
    }
}
