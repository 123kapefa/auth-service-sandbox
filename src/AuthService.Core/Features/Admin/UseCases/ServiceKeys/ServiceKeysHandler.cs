using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.Domain.AuditLog;
using AuthService.Domain.ServiceKeys;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlatformAuth;
using PlatformAuth.Revocation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.ServiceKeys;

/// <summary>
/// Сервисы проекта и их ключи доступа. Ключ — клиент Keycloak: сервис входа регистрирует его по разрешению техника
/// и показывает секрет один раз. Перевыпуск и отзыв сразу отзывают и уже выданные сервису токены (отметка по его учётке).
/// Права сервиса в сервисе входа (service.*) — роли клиента приложения сервиса входа у учётки сервиса; в его токене —
/// в permissions (маппер плагина). Права в других приложениях проекта («notifications:notifications.send») — роли-права
/// клиента приложения у той же учётки: в токене они в app_permissions, а приложение — в aud (ServiceKeyPermissions).
/// Права сужены — старые токены отзываются, сервис возьмёт новый.
/// Частичные сбои: Keycloak изменён, а база нет (или наоборот) — порядок шагов такой, чтобы ключ не остался «ничьим»
/// и выданные токены не жили дольше, чем надо; что не удалось исправить — в лог (для техника).
/// </summary>
public sealed partial class ServiceKeysHandler(
    IServiceKeyStore store,
    IValidator<IssueServiceKeyRequest> validator,
    IValidator<SetServicePermissionsRequest> permissionsValidator,
    IIdentityAdmin identityAdmin,
    IOptions<JwtAuthenticationOptions> authentication,
    IServiceRegistry registry,
    ITokenRevocationStore revocations,
    IDataProtectionProvider dataProtection,
    AdminAudit audit,
    CurrentUser caller,
    TimeProvider timeProvider,
    ILogger<ServiceKeysHandler> logger)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AuthService.ServiceKeys.RegistrationToken");

    public async Task<Result<IReadOnlyList<ServiceKeyResponse>, Error>> ListAsync(CancellationToken cancellationToken)
    {
        List<ServiceKeyResponse> list = [];
        foreach (ServiceKey key in await store.ListAsync(cancellationToken))
        {
            // Права одного ключа не прочитать (учётку удалили в консоли) — список всё равно нужен, чтобы ключ отозвать.
            Result<IReadOnlyList<string>, Error> permissions = await PermissionsOfAsync(key, cancellationToken);
            if (permissions.IsFailure)
            {
                LogPermissionsUnavailable(logger, key.ClientId, permissions.Error.Messages[0].Code);
            }

            list.Add(ToResponse(key, permissions.IsSuccess ? permissions.Value : null));
        }

        return list;
    }

    public async Task<Result<IssuedServiceKeyResponse, Error>> IssueAsync(IssueServiceKeyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        if (!registry.IsConfigured)
        {
            return ServiceRegistryErrors.NotConfigured();
        }

        string code = request.Code!;
        string name = request.Name!.Trim();
        if (await store.FindAsync(code, cancellationToken) is not null)
        {
            return ServiceKeyErrors.Exists(code);
        }

        UnitResult<Error> known = await CheckAppPermissionsAsync(Normalize(request.Permissions), cancellationToken);
        if (known.IsFailure)
        {
            return known.Error;
        }

        Result<RegisteredService, Error> registered = await registry.RegisterAsync(ServiceKey.ClientIdOf(code), name, cancellationToken);
        if (registered.IsFailure)
        {
            return registered.Error;
        }

        string clientId = ServiceKey.ClientIdOf(code);
        IReadOnlyList<string> permissions = Normalize(request.Permissions);
        if (permissions.Count > 0)
        {
            UnitResult<Error> granted = await ApplyAsync(registered.Value.ServiceAccountId, permissions, [], cancellationToken);
            if (granted.IsFailure)
            {
                // Без прав ключ выдавать не будем: клиент в Keycloak убираем, чтобы не остался «ничей».
                await UndoRegistrationAsync(clientId, registered.Value.RegistrationToken);
                return granted.Error;
            }
        }

        ServiceKey key = ServiceKey.Issue(
            code,
            name,
            registered.Value.ServiceAccountId,
            _protector.Protect(registered.Value.RegistrationToken),
            timeProvider.GetUtcNow(),
            caller.Id);
        try
        {
            await store.AddAsync(key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Запись не сохранилась (например, одновременная выдача того же кода) — без неё ключом не управлять.
            LogStoreFailed(logger, clientId, exception);
            await UndoRegistrationAsync(clientId, registered.Value.RegistrationToken);
            return ServiceKeyErrors.NotSaved(code);
        }

        await RecordAsync(AuditActions.SERVICE_KEY_ISSUED, key, new { name, permissions }, cancellationToken);
        return new IssuedServiceKeyResponse(ToResponse(key, permissions), registered.Value.ClientSecret);
    }

    /// <summary>
    /// Новый секрет. Старый Keycloak сразу перестаёт принимать. Выданные по старому токены — по умолчанию тоже сразу
    /// (ключ мог утечь); keepTokens — плановая смена: они доживают свой срок (минуты), и сервис, который читает секрет
    /// из файла (ServiceToken:ClientSecretFile), подхватывает новый без остановки и без перезапуска.
    /// </summary>
    public async Task<Result<IssuedServiceKeyResponse, Error>> RotateAsync(string code, bool keepTokens, CancellationToken cancellationToken)
    {
        ServiceKey? key = await store.FindAsync(code, cancellationToken);
        if (key is null)
        {
            return ServiceKeyErrors.NotFound(code);
        }

        Result<string, Error> registrationToken = Unprotect(key);
        if (registrationToken.IsFailure)
        {
            return registrationToken.Error;
        }

        Result<RotatedSecret, Error> rotated = await registry.RotateSecretAsync(key.ClientId, registrationToken.Value, cancellationToken);
        if (rotated.IsFailure)
        {
            return rotated.Error;
        }

        // Keycloak уже сменил секрет и токен управления: новый токен — в базу сразу. Не сохранился — секрет всё равно
        // отдаём (иначе сервис останется без рабочего ключа), а техник по логу восстановит управление ключом.
        DateTimeOffset now = timeProvider.GetUtcNow();
        key.Rotated(_protector.Protect(rotated.Value.RegistrationToken), now);
        try
        {
            await store.SaveAsync(key, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogStoreFailed(logger, key.ClientId, exception);
        }

        // Старый ключ мог утечь: выданные по нему токены — тоже недействительны. Плановая смена — пусть доживут.
        if (!keepTokens)
        {
            await RevokeTokensAsync(key, now, CancellationToken.None);
        }

        await RecordAsync(AuditActions.SERVICE_KEY_ROTATED, key, new { keepTokens }, CancellationToken.None);
        Result<IReadOnlyList<string>, Error> permissions = await PermissionsOfAsync(key, CancellationToken.None);
        return new IssuedServiceKeyResponse(ToResponse(key, permissions.IsSuccess ? permissions.Value : null), rotated.Value.ClientSecret);
    }

    public async Task<Result<ServiceKeyResponse, Error>> SetPermissionsAsync(
        string code,
        SetServicePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidationResult validation = await permissionsValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        ServiceKey? key = await store.FindAsync(code, cancellationToken);
        if (key is null)
        {
            return ServiceKeyErrors.NotFound(code);
        }

        Result<IReadOnlyList<string>, Error> current = await PermissionsOfAsync(key, cancellationToken);
        if (current.IsFailure)
        {
            return current.Error;
        }

        IReadOnlyList<string> wanted = Normalize(request.Permissions);
        string[] grant = [.. wanted.Except(current.Value, StringComparer.Ordinal)];
        string[] revoke = [.. current.Value.Except(wanted, StringComparer.Ordinal)];
        UnitResult<Error> known = await CheckAppPermissionsAsync(grant, cancellationToken);
        if (known.IsFailure)
        {
            return known.Error;
        }

        if (grant.Length > 0 || revoke.Length > 0)
        {
            UnitResult<Error> changed = await ApplyAsync(key.ServiceAccountId, grant, revoke, cancellationToken);
            if (changed.IsFailure)
            {
                return changed.Error;
            }

            if (revoke.Length > 0) // права сужены — токены с ними больше не действуют
            {
                await RevokeTokensAsync(key, timeProvider.GetUtcNow(), cancellationToken);
            }

            await RecordAsync(
                AuditActions.SERVICE_KEY_PERMISSIONS_CHANGED, key, new { permissions = new { old = current.Value, @new = wanted } }, cancellationToken);
        }

        return ToResponse(key, wanted);
    }

    public async Task<UnitResult<Error>> RevokeAsync(string code, CancellationToken cancellationToken)
    {
        ServiceKey? key = await store.FindAsync(code, cancellationToken);
        if (key is null)
        {
            return ServiceKeyErrors.NotFound(code);
        }

        // Сначала — отметка: выданные токены сервиса недействительны, что бы ни случилось дальше.
        await RevokeTokensAsync(key, timeProvider.GetUtcNow(), cancellationToken);

        Result<string, Error> registrationToken = Unprotect(key);
        UnitResult<Error> deleted = registrationToken.IsSuccess
            ? await registry.DeleteAsync(key.ClientId, registrationToken.Value, cancellationToken)
            : registrationToken.Error;
        bool unconfirmed = false;
        if (deleted.IsFailure)
        {
            // Токен управления не принят или не прочитан: клиент мог быть уже удалён (повторный отзыв после сбоя) или
            // изменён вне сервиса входа. Запись убираем, чтобы ключ не висел в админке; клиент — проверить технику.
            if (!ServiceKeyErrors.IsManagementLost(deleted.Error))
            {
                return deleted;
            }

            unconfirmed = true;
            LogClientDeletionUnconfirmed(logger, key.ClientId, deleted.Error.Messages[0].Code);
        }

        await store.RemoveAsync(key, cancellationToken);
        await RecordAsync(AuditActions.SERVICE_KEY_REVOKED, key, new { name = key.Name, clientDeletionUnconfirmed = unconfirmed }, cancellationToken);
        return UnitResult.Success<Error>();
    }

    private Result<string, Error> Unprotect(ServiceKey key)
    {
        try
        {
            return _protector.Unprotect(key.ProtectedRegistrationToken);
        }
        catch (CryptographicException exception)
        {
            // Ключи DataProtection потеряны или сменились — токен управления не прочитать.
            LogUnreadable(logger, key.ClientId, exception);
            return ServiceKeyErrors.Unreadable(key.Code);
        }
    }

    // Откат выдачи: клиента — удалить его же токеном. Не вышло — в лог: клиент в Keycloak остался без записи.
    private async Task UndoRegistrationAsync(string clientId, string registrationToken)
    {
        UnitResult<Error> undone = await registry.DeleteAsync(clientId, registrationToken, CancellationToken.None);
        if (undone.IsFailure)
        {
            LogOrphanClient(logger, clientId, undone.Error.Messages[0].Code);
        }
    }

    // Токены сервиса — отметкой «по приложению» (клиент сервиса + его учётка): она включает и токены той же секунды.
    // Отметка «по пользователю» их пропустила бы — у токенов сервиса нет сессии, которая закрыла бы ту же секунду.
    private Task RevokeTokensAsync(ServiceKey key, DateTimeOffset at, CancellationToken cancellationToken) =>
        revocations.RevokeAppAsync(key.ClientId, key.ServiceAccountId.ToString(), at, cancellationToken);

    private Task RecordAsync(string action, ServiceKey key, object changes, CancellationToken cancellationToken) =>
        audit.RecordAsync(action, AuditActions.SERVICE_OBJECT, key.Id, key.ClientId, changes, cancellationToken);

    /// <summary>Клиент приложения сервиса входа: на нём права сервисов (service.*).</summary>
    private string AppClientId => authentication.Value.Audience;

    /// <summary>
    /// Права, которые можно выдать ключу в приложениях проекта: права (kind=permission) каждого приложения, кроме сервиса
    /// входа (там — только service.*). Приложения без прав не попадают.
    /// </summary>
    public async Task<Result<IReadOnlyList<GrantableAppPermissionsResponse>, Error>> GrantableAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ProjectApp>, Error> apps = await ProjectAppsAsync(cancellationToken);
        return apps.IsFailure
            ? apps.Error
            : Result.Success<IReadOnlyList<GrantableAppPermissionsResponse>, Error>(
            [
                .. apps.Value.Select(app => new GrantableAppPermissionsResponse(
                    app.ClientId,
                    app.Name,
                    [.. app.Permissions.Select(permission => new GrantablePermissionResponse(
                        ServiceKeyPermissions.Qualify(app.ClientId, permission.Name), permission.Name, permission.Description))])),
            ]);
    }

    private async Task<Result<IReadOnlyList<string>, Error>> PermissionsOfAsync(ServiceKey key, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<string>, Error> roles = await identityAdmin.GetClientRolesAsync(key.ServiceAccountId, AppClientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        Result<IReadOnlyList<ProjectApp>, Error> apps = await ProjectAppsAsync(cancellationToken);
        if (apps.IsFailure)
        {
            return apps.Error;
        }

        List<string> permissions = [.. roles.Value];
        foreach (ProjectApp app in apps.Value)
        {
            Result<IReadOnlyList<string>, Error> granted = await identityAdmin.GetClientRolesAsync(key.ServiceAccountId, app.ClientId, cancellationToken);
            if (granted.IsFailure)
            {
                return granted.Error;
            }

            permissions.AddRange(granted.Value
                .Where(name => app.Permissions.Any(permission => string.Equals(permission.Name, name, StringComparison.Ordinal)))
                .Select(name => ServiceKeyPermissions.Qualify(app.ClientId, name)));
        }

        return Result.Success<IReadOnlyList<string>, Error>(Normalize(permissions));
    }

    // Выдать и снять права: service.* — в клиенте сервиса входа, «приложение:право» — в клиенте приложения.
    private async Task<UnitResult<Error>> ApplyAsync(Guid serviceAccountId, IReadOnlyCollection<string> grant, IReadOnlyCollection<string> revoke, CancellationToken cancellationToken)
    {
        foreach (string app in grant.Concat(revoke).Select(AppOf).Distinct(StringComparer.Ordinal))
        {
            UnitResult<Error> changed = await identityAdmin.SetClientRolesAsync(
                serviceAccountId,
                app,
                [.. grant.Where(permission => string.Equals(AppOf(permission), app, StringComparison.Ordinal)).Select(NameOf)],
                [.. revoke.Where(permission => string.Equals(AppOf(permission), app, StringComparison.Ordinal)).Select(NameOf)],
                cancellationToken);
            if (changed.IsFailure)
            {
                return changed;
            }
        }

        return UnitResult.Success<Error>();
    }

    // Права в приложениях — только существующие права (kind=permission) подключённых приложений.
    private async Task<UnitResult<Error>> CheckAppPermissionsAsync(IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        if (!permissions.Any(ServiceKeyPermissions.IsAppPermission))
        {
            return UnitResult.Success<Error>();
        }

        Result<IReadOnlyList<ProjectApp>, Error> apps = await ProjectAppsAsync(cancellationToken);
        if (apps.IsFailure)
        {
            return apps.Error;
        }

        HashSet<string> known = [.. apps.Value.SelectMany(app => app.Permissions.Select(permission => ServiceKeyPermissions.Qualify(app.ClientId, permission.Name)))];
        string[] unknown = [.. permissions.Where(ServiceKeyPermissions.IsAppPermission).Where(permission => !known.Contains(permission))];
        return unknown.Length == 0 ? UnitResult.Success<Error>() : ServiceKeyErrors.UnknownPermissions(unknown);
    }

    // Приложения проекта с правами (кроме сервиса входа): клиенты, видимые сервису входа, у которых есть роли-права.
    private async Task<Result<IReadOnlyList<ProjectApp>, Error>> ProjectAppsAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ClientInfo>, Error> clients = await identityAdmin.GetClientsAsync(cancellationToken);
        if (clients.IsFailure)
        {
            return clients.Error;
        }

        List<ProjectApp> apps = [];
        foreach (ClientInfo client in clients.Value.Where(client => !string.Equals(client.ClientId, AppClientId, StringComparison.Ordinal)))
        {
            Result<IReadOnlyList<AppRole>, Error> roles = await identityAdmin.GetAppRolesAsync(client.ClientId, cancellationToken);
            AppRole[] permissions = roles.IsSuccess ? [.. roles.Value.Where(AppCatalog.IsPermission).OrderBy(role => role.Name, StringComparer.Ordinal)] : [];
            if (permissions.Length > 0)
            {
                apps.Add(new ProjectApp(client.ClientId, client.Name, permissions));
            }
        }

        return apps.OrderBy(app => app.ClientId, StringComparer.Ordinal).ToList();
    }

    private string AppOf(string permission) =>
        ServiceKeyPermissions.TryParse(permission, out string app, out _) ? app : AppClientId;

    private static string NameOf(string permission) =>
        ServiceKeyPermissions.TryParse(permission, out _, out string name) ? name : permission;

    // Только права сервисов и права в приложениях, без повторов, по алфавиту.
    private static IReadOnlyList<string> Normalize(IEnumerable<string>? permissions) =>
        [.. (permissions ?? []).Where(permission => AuthServicePermissions.ServiceAccess.All.Contains(permission, StringComparer.Ordinal)
                                                    || ServiceKeyPermissions.IsAppPermission(permission))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private sealed record ProjectApp(string ClientId, string? Name, IReadOnlyList<AppRole> Permissions);

    private static ServiceKeyResponse ToResponse(ServiceKey key, IReadOnlyList<string>? permissions) =>
        new(key.Code, key.ClientId, key.Name, key.IssuedAt, key.RotatedAt, permissions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Permissions of service key {ClientId} are unavailable: {Code}")]
    private static partial void LogPermissionsUnavailable(ILogger logger, string clientId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Service key {ClientId}: database save failed")]
    private static partial void LogStoreFailed(ILogger logger, string clientId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Keycloak client {ClientId} was registered but could not be removed after a failure ({Code}): remove it manually")]
    private static partial void LogOrphanClient(ILogger logger, string clientId, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Service key {ClientId} revoked without confirmed client deletion ({Code}): check the client in Keycloak")]
    private static partial void LogClientDeletionUnconfirmed(ILogger logger, string clientId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Registration token of service key {ClientId} cannot be decrypted")]
    private static partial void LogUnreadable(ILogger logger, string clientId, Exception exception);
}

/// <summary>
/// Права ключа: service.* — в сервисе входа, «приложение:право» — в приложении проекта (notifications:notifications.send).
/// Имя права уникально только внутри приложения, поэтому право в приложении — всегда с его кодом.
/// </summary>
public static class ServiceKeyPermissions
{
    public const char SEPARATOR = ':';

    public static string Qualify(string app, string permission) => app + SEPARATOR + permission;

    public static bool TryParse(string permission, out string app, out string name)
    {
        ArgumentNullException.ThrowIfNull(permission);
        int separator = permission.IndexOf(SEPARATOR, StringComparison.Ordinal);
        app = separator > 0 ? permission[..separator] : string.Empty;
        name = separator > 0 ? permission[(separator + 1)..] : string.Empty;
        return separator > 0 && name.Length > 0 && !name.Contains(SEPARATOR, StringComparison.Ordinal)
            && System.Text.RegularExpressions.Regex.IsMatch(app, IssueServiceKeyRequestValidator.CODE_PATTERN);
    }

    public static bool IsAppPermission(string permission) => TryParse(permission, out _, out _);

    /// <summary>Право, которое можно выдать ключу: service.* или право в приложении.</summary>
    public static bool IsValid(string? permission) =>
        permission is not null && (AuthServicePermissions.ServiceAccess.All.Contains(permission, StringComparer.Ordinal) || IsAppPermission(permission));
}

public static class ServiceKeyErrors
{
    public static Error Exists(string code) =>
        Error.Conflict("services.exists", $"Сервис «{code}» уже есть.");

    public static Error NotFound(string code) =>
        Error.NotFound("services.not_found", $"Сервис «{code}» не найден.");

    public static Error NotSaved(string code) =>
        Error.Failure("services.not_saved", $"Ключ сервиса «{code}» не сохранён — выдача отменена, попробуйте ещё раз.").AsTransient();

    /// <summary>Токен управления ключом не расшифровать (сменились ключи DataProtection).</summary>
    public static Error Unreadable(string code) =>
        Error.Conflict("services.management_unreadable", $"Ключом сервиса «{code}» не управлять: токен управления не расшифровать. Отзовите ключ и выдайте новый.");

    /// <summary>Таких прав в приложениях проекта нет (опечатка, приложение не подключено или право не объявлено).</summary>
    public static Error UnknownPermissions(IReadOnlyList<string> permissions) =>
        Error.Validation("services.unknown_permissions", $"Таких прав в приложениях проекта нет: {string.Join(", ", permissions)}.", "permissions");

    /// <summary>Ключом больше не управлять через Keycloak (токен управления не принят или не прочитан).</summary>
    public static bool IsManagementLost(Error error) =>
        error is not null && error.Messages.Count > 0
        && (string.Equals(error.Messages[0].Code, ServiceRegistryErrors.MANAGEMENT_REJECTED, StringComparison.Ordinal)
            || string.Equals(error.Messages[0].Code, "services.management_unreadable", StringComparison.Ordinal));
}
