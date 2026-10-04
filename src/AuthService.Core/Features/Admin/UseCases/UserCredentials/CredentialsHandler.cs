using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Features.Admin.UseCases.AppUserRoles;
using AuthService.Core.Features.Admin.UseCases.GetUser;
using AuthService.Domain.AuditLog;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Features.Admin.UseCases.UserCredentials;

/// <summary>
/// Пароль и 2FA пользователя — уровень аккаунта, только админ проекта. Пароль админ не видит и не задаёт: Keycloak
/// шлёт пользователю письмо со ссылкой (действие UPDATE_PASSWORD). 2FA — удалить приложение-аутентификатор и коды
/// восстановления (после недавнего входа: это снимает защиту аккаунта); включит заново сам пользователь. Свои — в
/// «Настройках входа».
/// </summary>
public sealed class CredentialsHandler(
    IIdentityAdmin identityAdmin,
    AdminTargets targets,
    AdminAudit audit,
    TimeProvider timeProvider,
    GetUserHandler getUser)
{
    /// <summary>Сколько живёт ссылка из письма.</summary>
    public static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromHours(24);

    public async Task<Result<UserDetailResponse, Error>> SendPasswordResetAsync(
        RoleChangeActor actor,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        Result<IdentityUser, Error> user = await TargetAsync(actor, userId, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        if (string.IsNullOrWhiteSpace(user.Value.Email))
        {
            return AdminErrors.NoEmail();
        }

        UnitResult<Error> sent = await identityAdmin.SendActionsEmailAsync(
            userId, [Credentials.UPDATE_PASSWORD], ResetLinkLifetime, cancellationToken);
        if (sent.IsFailure)
        {
            return sent.Error;
        }

        await audit.RecordAsync(AuditActions.USER_PASSWORD_RESET_REQUESTED, user.Value, new { email = user.Value.Email }, cancellationToken);
        return await getUser.Handle(userId, onlyCustomers: false, cancellationToken);
    }

    public async Task<Result<UserDetailResponse, Error>> DisableTwoFactorAsync(
        RoleChangeActor actor,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        Result<IdentityUser, Error> user = await TargetAsync(actor, userId, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        if (actor.AuthenticatedAt is not { } loggedIn || timeProvider.GetUtcNow() - loggedIn > UserRolesHandler.RecentLogin)
        {
            return AdminErrors.ReauthenticationRequired();
        }

        Result<int, Error> removed = await identityAdmin.RemoveCredentialsAsync(userId, Credentials.TwoFactor, cancellationToken);
        if (removed.IsFailure)
        {
            return removed.Error;
        }

        if (removed.Value > 0) // 2FA и так не было — без записи в журнал
        {
            await audit.RecordAsync(AuditActions.USER_TWO_FACTOR_DISABLED, user.Value, new { removed = removed.Value }, cancellationToken);
        }

        return await getUser.Handle(userId, onlyCustomers: false, cancellationToken);
    }

    private async Task<Result<IdentityUser, Error>> TargetAsync(RoleChangeActor actor, Guid userId, CancellationToken cancellationToken) =>
        actor.Id == userId
            ? AdminErrors.SelfCredentials()
            : await targets.FindAsync(userId, onlyCustomers: false, cancellationToken);
}
