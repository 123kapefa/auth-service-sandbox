using System.Text.Json;
using AuthService.Contracts.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using PlatformAuth;

namespace AuthService.Core.Features.Auth.UseCases.Refresh;

/// <summary>
/// Что изменилось в доступе: права активного режима, роли и режим — прошлый access-токен против нового. Только для
/// показа пользователю: подпись не проверяется (прошлый — из своей HttpOnly cookie, новый — только что от Keycloak),
/// решения о доступе по ним не принимаются.
/// </summary>
public static class AccessDiff
{
    public static AccessChangesResponse? Between(string? previousToken, string currentToken)
    {
        if (Read(previousToken) is not { } before || Read(currentToken) is not { } after)
        {
            return null;
        }

        var changes = new AccessChangesResponse(
            [.. after.Permissions.Except(before.Permissions, StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. before.Permissions.Except(after.Permissions, StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. after.Roles.Except(before.Roles, StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. before.Roles.Except(after.Roles, StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            after.ActiveMode);
        bool same = changes.AddedPermissions.Count == 0 && changes.RemovedPermissions.Count == 0
            && changes.AddedRoles.Count == 0 && changes.RemovedRoles.Count == 0
            && string.Equals(before.ActiveMode, after.ActiveMode, StringComparison.Ordinal);
        return same ? null : changes;
    }

    private static Access? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            JsonWebToken jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
            return new Access(Strings(jwt, AuthClaims.PERMISSIONS), Strings(jwt, AuthClaims.AVAILABLE_ROLES),
                jwt.TryGetPayloadValue(AuthClaims.ACTIVE_MODE, out string? mode) ? mode : null);
        }
        catch (ArgumentException)
        {
            return null; // не JWT — сравнивать не с чем
        }
    }

    private static string[] Strings(JsonWebToken jwt, string claim) =>
        jwt.TryGetPayloadValue(claim, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];

    private sealed record Access(string[] Permissions, string[] Roles, string? ActiveMode);
}
