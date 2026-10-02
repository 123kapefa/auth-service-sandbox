using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin;

/// <summary>
/// Приложения, которыми вызвавший может управлять: у админа проекта — все (права из токена), у админа приложения —
/// те, где среди его ролей есть права управления (как в AppPermissionFilter). Приложение — клиент с ролями.
/// </summary>
public sealed class ManagedApps(IIdentityAdmin identityAdmin, AppCatalog catalog, IOptions<JwtAuthenticationOptions> authentication)
{
    /// <summary>Права, которые дают управлять приложением: роли, роли пользователей, доступ к нему, его журнал.</summary>
    public static IReadOnlyList<string> AppPermissions { get; } =
    [
        AuthServicePermissions.Roles.MANAGE,
        AuthServicePermissions.Users.MANAGE_ROLES,
        AuthServicePermissions.Users.BLOCK,
        AuthServicePermissions.Audit.VIEW,
    ];

    public async Task<Result<IReadOnlyList<ManagedAppResponse>, Error>> ListAsync(CurrentUser caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        Result<IReadOnlyList<ClientInfo>, Error> clients = await identityAdmin.GetClientsAsync(cancellationToken);
        if (clients.IsFailure)
        {
            return clients.Error;
        }

        string[] project = [.. AppPermissions.Where(caller.HasPermission)];
        List<ManagedAppResponse> apps = [];
        foreach (ClientInfo client in clients.Value)
        {
            if ((await catalog.LoadAsync(client.ClientId, cancellationToken)).IsFailure)
            {
                continue; // клиент без ролей — только для входа, не приложение с ролями
            }

            IReadOnlyList<string> own = [];
            if (caller.Id is { } id && !string.Equals(client.ClientId, authentication.Value.Audience, StringComparison.Ordinal))
            {
                Result<IReadOnlyList<string>, Error> effective = await identityAdmin.GetEffectiveClientRolesAsync(id, client.ClientId, cancellationToken);
                own = effective.IsSuccess ? [.. AppPermissions.Where(permission => effective.Value.Contains(permission, StringComparer.Ordinal))] : [];
            }

            string[] permissions = [.. project.Union(own, StringComparer.Ordinal)];
            if (permissions.Length > 0)
            {
                apps.Add(new ManagedAppResponse(client.ClientId, client.Name, permissions, project.Length > 0));
            }
        }

        return apps;
    }
}
