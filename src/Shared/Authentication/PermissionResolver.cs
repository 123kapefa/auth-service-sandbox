using System.Collections.Frozen;
using System.Security.Claims;

namespace PlatformAuth;

public interface IPermissionResolver
{
    /// <summary>Права пользователя в его активном режиме.</summary>
    IReadOnlySet<string> GetPermissions(ClaimsPrincipal user);
}

/// <summary>
/// Права — из claim permissions токена: их кладёт сервер входа (маппер приложения), уже по активному режиму —
/// админ в режиме покупателя получает права покупателя. Карта «роль → права» живёт в Keycloak, а не в коде сервисов.
/// </summary>
internal sealed class TokenPermissionResolver : IPermissionResolver
{
    public IReadOnlySet<string> GetPermissions(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindAll(AuthClaims.PERMISSIONS).Select(claim => claim.Value).ToFrozenSet(StringComparer.Ordinal);
    }
}
