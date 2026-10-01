using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin.Catalog;

/// <summary>
/// При старте: права из кода (AuthServicePermissions.Catalog) → роли клиента приложения в Keycloak (kind=permission,
/// source=code). Недостающие создаются, у существующих обновляется описание; права, которых в коде больше нет, не
/// удаляются — они могут входить в роли (только предупреждение в лог). Keycloak ещё не готов — повтор через паузу.
/// </summary>
public sealed partial class PermissionCatalogSync(
    IServiceScopeFactory scopes,
    IOptions<JwtAuthenticationOptions> authentication,
    ILogger<PermissionCatalogSync> logger) : BackgroundService
{
    private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(5);
    private const int MAX_ATTEMPTS = 24; // ~2 минуты: стенд поднимается дольше сервиса

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (int attempt = 1; attempt <= MAX_ATTEMPTS && !stoppingToken.IsCancellationRequested; attempt++)
        {
            UnitResult<Error> synced = await SyncOnceAsync(stoppingToken);
            if (synced.IsSuccess)
            {
                return;
            }

            LogRetry(logger, attempt, synced.Error.Messages[0].Message);
            await Task.Delay(_retryDelay, stoppingToken);
        }

        LogGaveUp(logger);
    }

    /// <summary>Одна сверка каталога с Keycloak.</summary>
    public async Task<UnitResult<Error>> SyncOnceAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        IIdentityAdmin identityAdmin = scope.ServiceProvider.GetRequiredService<IIdentityAdmin>();
        string clientId = authentication.Value.Audience;

        Result<IReadOnlyList<AppRole>, Error> existing = await identityAdmin.GetAppRolesAsync(clientId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        Dictionary<string, AppRole> byName = existing.Value.ToDictionary(role => role.Name, StringComparer.Ordinal);
        foreach (PermissionDefinition permission in AuthServicePermissions.Catalog)
        {
            var wanted = new AppRole(null, permission.Name, permission.Description, AppRoleKinds.PERMISSION, AppRoleKinds.SOURCE_CODE, false);
            UnitResult<Error> result = UnitResult.Success<Error>();
            if (!byName.TryGetValue(permission.Name, out AppRole? found))
            {
                result = await identityAdmin.CreateAppRoleAsync(clientId, wanted, cancellationToken);
                LogCreated(logger, permission.Name);
            }
            else if (!string.Equals(found.Description, permission.Description, StringComparison.Ordinal)
                     || !string.Equals(found.Kind, AppRoleKinds.PERMISSION, StringComparison.Ordinal)
                     || !string.Equals(found.Source, AppRoleKinds.SOURCE_CODE, StringComparison.Ordinal))
            {
                result = await identityAdmin.UpdateAppRoleAsync(clientId, wanted, cancellationToken);
            }

            if (result.IsFailure)
            {
                return result;
            }
        }

        HashSet<string> declared = [.. AuthServicePermissions.Catalog.Select(permission => permission.Name)];
        foreach (AppRole orphan in existing.Value.Where(role =>
                     string.Equals(role.Source, AppRoleKinds.SOURCE_CODE, StringComparison.Ordinal) && !declared.Contains(role.Name)))
        {
            LogOrphan(logger, orphan.Name);
        }

        LogSynced(logger, AuthServicePermissions.Catalog.Count, clientId);
        return UnitResult.Success<Error>();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Permission catalog synced: {Count} code permissions in client {ClientId}")]
    private static partial void LogSynced(ILogger logger, int count, string clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Permission {Name} created in Keycloak from the code catalog")]
    private static partial void LogCreated(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Permission {Name} is marked source=code but no longer declared in code; it is kept")]
    private static partial void LogOrphan(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Permission catalog sync attempt {Attempt} failed: {Reason}")]
    private static partial void LogRetry(ILogger logger, int attempt, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Permission catalog sync gave up; permissions from code may be missing in Keycloak")]
    private static partial void LogGaveUp(ILogger logger);
}
