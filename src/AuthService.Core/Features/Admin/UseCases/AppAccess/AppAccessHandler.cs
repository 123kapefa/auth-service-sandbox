using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;
using Validation;
using PlatformAuth.Revocation;
using AuthService.Domain.AuditLog;

namespace AuthService.Core.Features.Admin.UseCases.AppAccess;

/// <summary>
/// Доступ к приложению: аккаунт рабочий, но в одно приложение человек не входит. В Keycloak — роль access-denied
/// клиента приложения; вход и выдачу токенов этому приложению запрещает плагин (шаг входа и политика клиентов),
/// refresh уже открытой сессии этого приложения перестаёт работать, остальные приложения — как прежде.
/// </summary>
public sealed class AppAccessHandler(
    IIdentityAdmin identityAdmin,
    IValidator<SetAppAccessRequest> validator,
    AdminTargets targets,
    ITokenRevocationStore revocations,
    AdminAudit audit,
    TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<AppAccessResponse>, Error>> GetAsync(
        Guid userId,
        bool onlyCustomers,
        CancellationToken cancellationToken)
    {
        Result<IdentityUser, Error> user = await targets.FindAsync(userId, onlyCustomers, cancellationToken);
        return user.IsFailure ? user.Error : await ListAsync(userId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<AppAccessResponse>, Error>> SetAsync(
        Guid? callerId,
        Guid userId,
        string clientId,
        SetAppAccessRequest request,
        bool onlyCustomers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        if (callerId == userId)
        {
            return AdminErrors.SelfAccess();
        }

        Result<IdentityUser, Error> user = await targets.FindAsync(userId, onlyCustomers, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        Result<IReadOnlyList<AppAccessInfo>, Error> apps = await identityAdmin.GetAppAccessAsync(userId, cancellationToken);
        if (apps.IsFailure)
        {
            return apps.Error;
        }

        AppAccessInfo? app = apps.Value.FirstOrDefault(found => string.Equals(found.ClientId, clientId, StringComparison.Ordinal));
        if (app is null)
        {
            return AdminErrors.AppNotFound();
        }

        bool hasAccess = request.HasAccess!.Value;
        if (app.HasAccess != hasAccess)
        {
            string[] role = [AppRoles.ACCESS_DENIED];
            UnitResult<Error> changed = await identityAdmin.SetClientRolesAsync(
                userId,
                clientId,
                grant: hasAccess ? [] : role,
                revoke: hasAccess ? role : [],
                cancellationToken);
            if (changed.IsFailure)
            {
                return changed.Error;
            }

            if (!hasAccess)
            {
                // Токены этого приложения (azp) — недействительны сразу; токены остальных приложений не трогаем.
                await revocations.RevokeAppAsync(clientId, userId.ToString(), timeProvider.GetUtcNow(), cancellationToken);
            }

            await audit.RecordAsync(
                AuditActions.USER_APP_ACCESS_CHANGED,
                user.Value,
                new { app = clientId, hasAccess = new { old = app.HasAccess, @new = hasAccess } },
                cancellationToken);
        }

        return await ListAsync(userId, cancellationToken);
    }

    private async Task<Result<IReadOnlyList<AppAccessResponse>, Error>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AppAccessInfo>, Error> apps = await identityAdmin.GetAppAccessAsync(userId, cancellationToken);
        return apps.IsFailure
            ? apps.Error
            : Result.Success<IReadOnlyList<AppAccessResponse>, Error>(
                [.. apps.Value.Select(app => new AppAccessResponse(app.ClientId, app.Name, app.HasAccess))]);
    }
}
