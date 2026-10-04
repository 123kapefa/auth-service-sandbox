using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace PlatformAuth;

/// <summary>
/// Кто вызвал: данные из токена текущего запроса и права в его активном режиме.
/// Только читает — заполнять middleware-ом не нужно. Внедряется в эндпоинты и хендлеры.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor, IPermissionResolver permissionResolver)
{
    private IReadOnlySet<string>? _permissions;

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    /// <summary>sub из токена. null — анонимный запрос или sub не UUID.</summary>
    public Guid? Id => Guid.TryParse(Principal.FindFirstValue(AuthClaims.SUBJECT), out Guid id) ? id : null;

    public string? Phone => Principal.FindFirstValue(AuthClaims.PHONE);

    public string? ActiveMode => Principal.FindFirstValue(AuthClaims.ACTIVE_MODE);

    public IReadOnlyList<string> Roles => [.. Principal.FindAll(AuthClaims.ROLE).Select(claim => claim.Value)];

    /// <summary>Id сессии сервера входа (claim sid). null — claim нет.</summary>
    public string? SessionId => Principal.FindFirstValue(AuthClaims.SESSION_ID);

    /// <summary>Когда пользователь вошёл (claim auth_time). null — claim нет.</summary>
    public DateTimeOffset? AuthenticatedAt =>
        long.TryParse(Principal.FindFirstValue(AuthClaims.AUTH_TIME), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    public IReadOnlyList<string> AvailableModes => [.. Principal.FindAll(AuthClaims.AVAILABLE_ROLES).Select(claim => claim.Value)];

    public IReadOnlySet<string> Permissions => _permissions ??= permissionResolver.GetPermissions(Principal);

    public bool HasPermission(string permission) => Permissions.Contains(permission);

    /// <summary>Вызывает сервис проекта по ключу доступа (не человек).</summary>
    public bool IsService => ServiceClientId is not null;

    /// <summary>client_id сервиса, вызвавшего по ключу; у человека — null.</summary>
    public string? ServiceClientId => Principal.FindFirstValue(AuthClaims.CLIENT_ID);

    private ClaimsPrincipal Principal => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
}
