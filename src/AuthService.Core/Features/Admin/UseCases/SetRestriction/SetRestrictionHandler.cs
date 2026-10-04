using System.Globalization;
using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Features.Admin.UseCases.GetUser;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;
using Validation;
using AuthService.Domain.AuditLog;

namespace AuthService.Core.Features.Admin.UseCases.SetRestriction;

/// <summary>
/// Блокировка аккаунта целиком (во всём проекте): выключить пользователя в Keycloak, записать причину и время,
/// завершить все его сессии — refresh и новый вход больше не работают; уже выданные access-токены отзываются сразу
/// (отметка в Redis, проверка в Shared/Authentication). Правила боевого сервиса: менеджер — только покупателей,
/// себя — нельзя, повторная блокировка — тихий успех.
/// </summary>
public sealed class SetRestrictionHandler(
    IIdentityAdmin identityAdmin,
    IValidator<SetRestrictionRequest> validator,
    AdminTargets targets,
    AccessRevoker revoker,
    AdminAudit audit,
    TimeProvider timeProvider,
    GetUserHandler getUser)
{
    public async Task<Result<UserDetailResponse, Error>> Handle(
        Guid? callerId,
        Guid userId,
        SetRestrictionRequest request,
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
            return AdminErrors.SelfRestriction();
        }

        Result<IdentityUser, Error> user = await targets.FindAsync(userId, onlyCustomers, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        bool restricted = request.Restricted!.Value;
        UnitResult<Error> changed = restricted
            ? await BlockAsync(user.Value, request.Reason, cancellationToken)
            : await UnblockAsync(user.Value, cancellationToken);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        if (restricted == user.Value.Enabled)
        {
            await audit.RecordAsync(
                AuditActions.USER_RESTRICTION_CHANGED,
                user.Value,
                new { restricted = new { old = !user.Value.Enabled, @new = restricted }, reason = restricted ? request.Reason?.Trim() : null },
                cancellationToken);
        }

        return await getUser.Handle(userId, onlyCustomers: false, cancellationToken);
    }

    private async Task<UnitResult<Error>> BlockAsync(IdentityUser user, string? reason, CancellationToken cancellationToken)
    {
        if (!user.Enabled)
        {
            return UnitResult.Success<Error>(); // уже заблокирован — как в боевом сервисе, без ошибки
        }

        UnitResult<Error> disabled = await identityAdmin.UpdateUserAsync(
            user.Id,
            new UserChanges(false, new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [UserAttributes.RESTRICTION_REASON] = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                [UserAttributes.RESTRICTED_AT] = timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
            }),
            cancellationToken);

        if (disabled.IsFailure)
        {
            return disabled;
        }

        // Сессии завершаются, а уже выданные access-токены — недействительны сразу, а не через 15 минут.
        return await revoker.EndAllSessionsAsync(user.Id, cancellationToken);
    }

    private async Task<UnitResult<Error>> UnblockAsync(IdentityUser user, CancellationToken cancellationToken) =>
        user.Enabled
            ? UnitResult.Success<Error>()
            : await identityAdmin.UpdateUserAsync(
                user.Id,
                new UserChanges(true, new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [UserAttributes.RESTRICTION_REASON] = null,
                    [UserAttributes.RESTRICTED_AT] = null,
                }),
                cancellationToken);
}
