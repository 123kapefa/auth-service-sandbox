using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using AuthService.Domain.ServiceKeys;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.DataProtection;
using PlatformAuth;
using PlatformAuth.Revocation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.ServiceKeys;

/// <summary>
/// Сервисы проекта и их ключи доступа. Ключ — клиент Keycloak: сервис входа регистрирует его по разрешению техника
/// и показывает секрет один раз. Перевыпуск и отзыв сразу отзывают и уже выданные сервису токены (отметка по его учётке).
/// </summary>
public sealed class ServiceKeysHandler(
    IServiceKeyStore store,
    IValidator<IssueServiceKeyRequest> validator,
    IServiceRegistry registry,
    ITokenRevocationStore revocations,
    IDataProtectionProvider dataProtection,
    AdminAudit audit,
    CurrentUser caller,
    TimeProvider timeProvider)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AuthService.ServiceKeys.RegistrationToken");

    public async Task<IReadOnlyList<ServiceKeyResponse>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await store.ListAsync(cancellationToken)).Select(ToResponse)];

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

        ServiceKey key = ServiceKey.Issue(
            code,
            name,
            registered.Value.ServiceAccountId,
            _protector.Protect(registered.Value.RegistrationToken),
            timeProvider.GetUtcNow(),
            caller.Id);
        await store.AddAsync(key, cancellationToken);
        await RecordAsync(AuditActions.SERVICE_KEY_ISSUED, key, new { name }, cancellationToken);
        return new IssuedServiceKeyResponse(ToResponse(key), registered.Value.ClientSecret);
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
        await revocations.RevokeUserAsync(key.ServiceAccountId.ToString(), now, cancellationToken);
        await RecordAsync(AuditActions.SERVICE_KEY_ROTATED, key, new { }, cancellationToken);
        return new IssuedServiceKeyResponse(ToResponse(key), rotated.Value.ClientSecret);
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
        await revocations.RevokeUserAsync(key.ServiceAccountId.ToString(), timeProvider.GetUtcNow(), cancellationToken);
        await RecordAsync(AuditActions.SERVICE_KEY_REVOKED, key, new { name = key.Name }, cancellationToken);
        return UnitResult.Success<Error>();
    }

    private Task RecordAsync(string action, ServiceKey key, object changes, CancellationToken cancellationToken) =>
        audit.RecordAsync(action, AuditActions.SERVICE_OBJECT, key.Id, key.ClientId, changes, cancellationToken);

    private static ServiceKeyResponse ToResponse(ServiceKey key) =>
        new(key.Code, key.ClientId, key.Name, key.IssuedAt, key.RotatedAt);
}

public static class ServiceKeyErrors
{
    public static Error Exists(string code) =>
        Error.Conflict("services.exists", $"Сервис «{code}» уже есть.");

    public static Error NotFound(string code) =>
        Error.NotFound("services.not_found", $"Сервис «{code}» не найден.");
}
