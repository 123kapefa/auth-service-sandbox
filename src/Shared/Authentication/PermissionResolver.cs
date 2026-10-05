using System.Collections.Frozen;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PlatformAuth;

public interface IPermissionResolver
{
    /// <summary>Права пользователя в его активном режиме.</summary>
    IReadOnlySet<string> GetPermissions(ClaimsPrincipal user);
}

/// <summary>
/// Права — из claim permissions токена: их кладёт сервер входа (маппер приложения), уже по активному режиму —
/// админ в режиме покупателя получает права покупателя. Карта «роль → права» живёт в Keycloak, а не в коде сервисов.
/// <para>
/// Токен сервиса по ключу адресован всем приложениям, куда сервису можно (aud), а права в каждом — в app_permissions
/// по приложениям: сервис «Уведомления» (Audience = notifications) видит только права в «Уведомлениях», а не права
/// того же ключа в сервисе входа или в «Заказах». Своих прав в app_permissions нет — права из permissions (так их
/// видит приложение, чьи роли — в permissions, например сервис входа).
/// </para>
/// </summary>
internal sealed class TokenPermissionResolver(IOptions<JwtAuthenticationOptions> authentication) : IPermissionResolver
{
    public IReadOnlySet<string> GetPermissions(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.FindFirst(AuthClaims.CLIENT_ID) is not null
            && AppPermissionsOf(user, authentication.Value.Audience) is { } app)
        {
            return app;
        }

        return user.FindAll(AuthClaims.PERMISSIONS).Select(claim => claim.Value).ToFrozenSet(StringComparer.Ordinal);
    }

    // app_permissions — JSON-объект { приложение: [права] }; в ClaimsPrincipal он — одна claim с JSON в значении.
    private static FrozenSet<string>? AppPermissionsOf(ClaimsPrincipal user, string audience)
    {
        string? json = user.FindFirstValue(AuthClaims.APP_PERMISSIONS);
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(audience))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(audience, out JsonElement permissions)
                   && permissions.ValueKind == JsonValueKind.Array
                ? permissions.EnumerateArray()
                    .Where(permission => permission.ValueKind == JsonValueKind.String)
                    .Select(permission => permission.GetString()!)
                    .ToFrozenSet(StringComparer.Ordinal)
                : null;
        }
        catch (JsonException)
        {
            return null; // не JSON — как будто claim нет
        }
    }
}
