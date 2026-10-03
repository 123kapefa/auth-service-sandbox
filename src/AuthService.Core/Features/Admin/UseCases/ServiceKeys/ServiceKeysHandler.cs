using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.Domain.AuditLog;
using AuthService.Domain.ServiceKeys;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.DataProtection;
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
/// в permissions (маппер плагина). Права сужены — старые токены отзываются, сервис возьмёт новый.
/// </summary>
public sealed class ServiceKeysHandler(
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
    TimeProvider timeProvider)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AuthService.ServiceKeys.RegistrationToken");

    public async Task<Result<IReadOnlyList<ServiceKeyResponse>, Error>> ListAsync(CancellationToken cancellationToken)
    {
        List<ServiceKeyResponse> list = [];
        foreach (ServiceKey key in await store.ListAsync(cancellationToken))
        {
            Result<IReadOnlyList<string>, Error> permissions = await PermissionsOfAsync(key, cancellationToken);
            if (permissions.IsFailure)
            {
                return permissions.Error;
            }

            list.Add(ToResponse(key, permissions.Value));
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

        Result<RegisteredService, Error> registered = await registry.RegisterAsync(ServiceKey.ClientIdOf(code), name, cancellationToken);
        if (registered.IsFailure)
        {
            return registered.Error;
        }

        IReadOnlyList<string> permissions = Normalize(request.Permissions);
        if (permissions.Count > 0)
        {
            UnitResult<Error> granted = await identityAdmin.SetClientRolesAsync(
                registered.Value.ServiceAccountId, AppClientId, permissions, [], cancellationToken);
            if (granted.IsFailure)
            {
                // Без прав ключ выдавать не будем: клиент в Keycloak убираем, чтобы не остался «ничей».
                await registry.DeleteAsync(ServiceKey.ClientIdOf(code), registered.Value.RegistrationToken, cancellationToken);
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
        await store.AddAsync(key, cancellationToken);
        await RecordAsync(AuditActions.SERVICE_KEY_ISSUED, key, new { name, permissions }, cancellationToken);
        return new IssuedServiceKeyResponse(ToResponse(key, permissions), registered.Value.ClientSecret);
    }

    public async Task<Result<IssuedServiceKeyResponse, Error>> RotateAsync(string code, CancellationToken cancellationToken)
    {
        ServiceKey? key = await store.FindAsync(code, cancellationToken);
        if (key is null)
        {
            return ServiceKeyErrors.NotFound(code);
        }

        Result<RegisteredService, Error> rotated =
            await registry.RotateSecretAsync(key.ClientId, _protector.Unprotect(key.ProtectedRegistrationToken), cancellationToken);
        if (rotated.IsFailure)
        {
            return rotated.Error;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        key.Rotated(_protector.Protect(rotated.Value.RegistrationToken), now);
        await store.SaveAsync(key, cancellationToken);
        // Ключ перевыпускают, когда старый мог утечь: выданные по нему токены — тоже недействительны.
        await RevokeTokensAsync(key, now, cancellationToken);
        await RecordAsync(AuditActions.SERVICE_KEY_ROTATED, key, new { }, cancellationToken);
        Result<IReadOnlyList<string>, Error> permissions = await PermissionsOfAsync(key, cancellationToken);
        return permissions.IsSuccess
            ? new IssuedServiceKeyResponse(ToResponse(key, permissions.Value), rotated.Value.ClientSecret)
            : permissions.Error;
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
        if (grant.Length > 0 || revoke.Length > 0)
        {
            UnitResult<Error> changed = await identityAdmin.SetClientRolesAsync(key.ServiceAccountId, AppClientId, grant, revoke, cancellationToken);
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

        UnitResult<Error> deleted =
            await registry.DeleteAsync(key.ClientId, _protector.Unprotect(key.ProtectedRegistrationToken), cancellationToken);
        if (deleted.IsFailure)
        {
            return deleted;
        }

        await store.RemoveAsync(key, cancellationToken);
        await RevokeTokensAsync(key, timeProvider.GetUtcNow(), cancellationToken);
        await RecordAsync(AuditActions.SERVICE_KEY_REVOKED, key, new { name = key.Name }, cancellationToken);
        return UnitResult.Success<Error>();
    }

    // Токены сервиса — отметкой «по приложению» (клиент сервиса + его учётка): она включает и токены той же секунды.
    // Отметка «по пользователю» их пропустила бы — у токенов сервиса нет сессии, которая закрыла бы ту же секунду.
    private Task RevokeTokensAsync(ServiceKey key, DateTimeOffset at, CancellationToken cancellationToken) =>
        revocations.RevokeAppAsync(key.ClientId, key.ServiceAccountId.ToString(), at, cancellationToken);

    private Task RecordAsync(string action, ServiceKey key, object changes, CancellationToken cancellationToken) =>
        audit.RecordAsync(action, AuditActions.SERVICE_OBJECT, key.Id, key.ClientId, changes, cancellationToken);

    /// <summary>Клиент приложения сервиса входа: на нём права сервисов (service.*).</summary>
    private string AppClientId => authentication.Value.Audience;

    private async Task<Result<IReadOnlyList<string>, Error>> PermissionsOfAsync(ServiceKey key, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<string>, Error> roles = await identityAdmin.GetClientRolesAsync(key.ServiceAccountId, AppClientId, cancellationToken);
        return roles.IsSuccess ? Result.Success<IReadOnlyList<string>, Error>(Normalize(roles.Value)) : roles.Error;
    }

    // Только права сервисов, без повторов, по алфавиту.
    private static IReadOnlyList<string> Normalize(IEnumerable<string>? permissions) =>
        [.. (permissions ?? []).Where(permission => AuthServicePermissions.ServiceAccess.All.Contains(permission, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private static ServiceKeyResponse ToResponse(ServiceKey key, IReadOnlyList<string> permissions) =>
        new(key.Code, key.ClientId, key.Name, key.IssuedAt, key.RotatedAt, permissions);
}

public static class ServiceKeyErrors
{
    public static Error Exists(string code) =>
        Error.Conflict("services.exists", $"Сервис «{code}» уже есть.");

    public static Error NotFound(string code) =>
        Error.NotFound("services.not_found", $"Сервис «{code}» не найден.");
}
