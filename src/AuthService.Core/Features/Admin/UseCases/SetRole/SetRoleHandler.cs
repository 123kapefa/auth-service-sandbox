using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.Core.Features.Admin.UseCases.GetUser;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.SetRole;

/// <summary>Кто меняет роль: id и когда вошёл (auth_time).</summary>
public sealed record RoleChangeActor(Guid? Id, DateTimeOffset? AuthenticatedAt);

/// <summary>
/// Смена роли пользователя в приложении (роли клиента sandbox-api): одна роль сотрудника или покупатель.
/// Выдача admin — только после недавнего входа. Роль изменилась — сессии пользователя завершаются (как в боевом
/// сервисе), а недоступный теперь активный режим сбрасывается в customer.
/// </summary>
public sealed class SetRoleHandler(
    IIdentityAdmin identityAdmin,
    IValidator<SetRoleRequest> validator,
    IOptions<JwtAuthenticationOptions> authentication,
    AccessRevoker revoker,
    TimeProvider timeProvider,
    GetUserHandler getUser)
{
    /// <summary>Сколько после входа можно выдавать admin без повторного входа.</summary>
    public static readonly TimeSpan RecentLogin = TimeSpan.FromMinutes(5);

    /// <summary>Роль платформы по введённому (регистр и пробелы не важны) или null.</summary>
    public static string? Normalize(string? role) =>
        AuthModes.All.FirstOrDefault(known => string.Equals(known, role?.Trim(), StringComparison.OrdinalIgnoreCase));

    public async Task<Result<UserDetailResponse, Error>> Handle(
        RoleChangeActor actor,
        Guid userId,
        SetRoleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        string role = Normalize(request.Role)!;
        if (actor.Id == userId)
        {
            return AdminErrors.SelfRole();
        }

        if (string.Equals(role, AuthModes.ADMIN, StringComparison.Ordinal)
            && (actor.AuthenticatedAt is not { } loggedIn || timeProvider.GetUtcNow() - loggedIn > RecentLogin))
        {
            return AdminErrors.ReauthenticationRequired();
        }

        Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(userId, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        string clientId = authentication.Value.Audience;
        Result<IReadOnlyList<string>, Error> roles = await identityAdmin.GetClientRolesAsync(userId, clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        IReadOnlyList<string> current = StaffRoles.Of(roles.Value);
        string[] target = string.Equals(role, AuthModes.CUSTOMER, StringComparison.Ordinal) ? [] : [role];
        string[] grant = [.. target.Except(current, StringComparer.Ordinal)];
        string[] revoke = [.. current.Except(target, StringComparer.Ordinal)];
        if (grant.Length == 0 && revoke.Length == 0)
        {
            return await getUser.Handle(userId, onlyCustomers: false, cancellationToken); // уже такая роль
        }

        UnitResult<Error> changed = await ApplyAsync(user.Value, clientId, grant, revoke, target, cancellationToken);
        return changed.IsFailure ? changed.Error : await getUser.Handle(userId, onlyCustomers: false, cancellationToken);
    }

    private async Task<UnitResult<Error>> ApplyAsync(
        IdentityUser user,
        string clientId,
        string[] grant,
        string[] revoke,
        string[] target,
        CancellationToken cancellationToken)
    {
        UnitResult<Error> roles = await identityAdmin.SetClientRolesAsync(user.Id, clientId, grant, revoke, cancellationToken);
        if (roles.IsFailure)
        {
            return roles;
        }

        bool modeStillAvailable = user.ActiveMode is null
            || string.Equals(user.ActiveMode, AuthModes.CUSTOMER, StringComparison.Ordinal)
            || target.Contains(user.ActiveMode, StringComparer.Ordinal);
        if (!modeStillAvailable)
        {
            UnitResult<Error> reset = await identityAdmin.UpdateUserAsync(
                user.Id,
                new UserChanges(null, new Dictionary<string, string?>(StringComparer.Ordinal) { [AuthClaims.ACTIVE_MODE] = AuthModes.CUSTOMER }),
                cancellationToken);
            if (reset.IsFailure)
            {
                return reset;
            }
        }

        // Сессии завершаются, токены со старыми ролями — недействительны сразу.
        return await revoker.EndAllSessionsAsync(user.Id, cancellationToken);
    }
}
