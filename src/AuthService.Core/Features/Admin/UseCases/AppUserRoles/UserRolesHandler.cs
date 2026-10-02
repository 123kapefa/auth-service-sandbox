using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.Domain.AuditLog;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.AppUserRoles;

/// <summary>Кто меняет роли: id и когда вошёл (auth_time).</summary>
public sealed record RoleChangeActor(Guid? Id, DateTimeOffset? AuthenticatedAt);

/// <summary>
/// Роли пользователя в приложении: сколько угодно ролей, роль = режим. Выдача роли с управлением ролями
/// (users.roles.manage, roles.manage) — только после недавнего входа. Свои роли менять нельзя. Роли изменились —
/// умный рефреш (а не выход, как в боевом сервисе): старые токены сразу не принимаются, клиент обновляет их без
/// перелогина; недоступный теперь (или не выбранный) активный режим — роль по умолчанию, а без неё — первая роль.
/// </summary>
public sealed class UserRolesHandler(
    IIdentityAdmin identityAdmin,
    UserAppRoles userRoles,
    IValidator<SetUserRolesRequest> validator,
    IOptions<JwtAuthenticationOptions> authentication,
    AccessChanges accessChanges,
    AdminAudit audit,
    TimeProvider timeProvider)
{
    /// <summary>Сколько после входа можно выдавать роли с управлением ролями без повторного входа.</summary>
    public static readonly TimeSpan RecentLogin = TimeSpan.FromMinutes(5);

    /// <summary>Права, с которыми роль раздаёт права: её выдача требует недавнего входа.</summary>
    private static readonly string[] _privileged = [AuthServicePermissions.Users.MANAGE_ROLES, AuthServicePermissions.Roles.MANAGE];

    public async Task<Result<UserRolesResponse, Error>> GetAsync(string clientId, Guid userId, CancellationToken cancellationToken)
    {
        Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(userId, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        Result<UserRoles, Error> roles = await userRoles.LoadAsync(userId, clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        Dictionary<string, string?> catalog = roles.Value.Catalog.Where(AppCatalog.IsRole).ToDictionary(role => role.Name, role => role.Description, StringComparer.Ordinal);
        AssignableRoleResponse[] assignable =
        [
            .. StaffRoles.Sort(catalog.Keys.Except(roles.Value.Defaults, StringComparer.Ordinal))
                .Select(name => new AssignableRoleResponse(name, catalog[name])),
        ];
        return new UserRolesResponse(
            userId, roles.Value.Assigned, roles.Value.Defaults, roles.Value.Modes, ActiveMode(clientId, user.Value), roles.Value.HasAccess, assignable);
    }

    public async Task<Result<UserRolesResponse, Error>> SetAsync(
        RoleChangeActor actor,
        string clientId,
        Guid userId,
        SetUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        UnitResult<Error> changed = await ChangeAsync(actor, clientId, userId, request.Roles!, cancellationToken);
        return changed.IsFailure ? changed.Error : await GetAsync(clientId, userId, cancellationToken);
    }

    /// <summary>Выданные роли — ровно эти (роли по умолчанию в списке ничего не меняют).</summary>
    public async Task<UnitResult<Error>> ChangeAsync(
        RoleChangeActor actor,
        string clientId,
        Guid userId,
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(names);

        if (actor.Id == userId)
        {
            return AdminErrors.SelfRole();
        }

        Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(userId, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        Result<UserRoles, Error> roles = await userRoles.LoadAsync(userId, clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        string[] unknown = [.. names.Where(name => roles.Value.Find(name) is null)];
        if (unknown.Length > 0)
        {
            return AdminErrors.UnknownRoles(unknown);
        }

        IReadOnlyList<string> wanted = StaffRoles.Sort(names.Select(name => roles.Value.Find(name)!).Except(roles.Value.Defaults, StringComparer.Ordinal));
        string[] grant = [.. wanted.Except(roles.Value.Assigned, StringComparer.Ordinal)];
        string[] revoke = [.. roles.Value.Assigned.Except(wanted, StringComparer.Ordinal)];
        if (grant.Length == 0 && revoke.Length == 0)
        {
            return UnitResult.Success<Error>(); // уже такие роли
        }

        Result<bool, Error> privileged = await AnyPrivilegedAsync(clientId, grant, cancellationToken);
        if (privileged.IsFailure)
        {
            return privileged.Error;
        }

        if (privileged.Value && (actor.AuthenticatedAt is not { } loggedIn || timeProvider.GetUtcNow() - loggedIn > RecentLogin))
        {
            return AdminErrors.ReauthenticationRequired();
        }

        UnitResult<Error> applied = await ApplyAsync(clientId, user.Value, grant, revoke, StaffRoles.Sort(roles.Value.Defaults.Concat(wanted)), roles.Value.Defaults, cancellationToken);
        if (applied.IsFailure)
        {
            return applied;
        }

        await audit.RecordAsync(
            AuditActions.USER_ROLE_CHANGED,
            user.Value,
            new { app = clientId, role = new { old = StaffRoles.Describe(roles.Value.Assigned), @new = StaffRoles.Describe(wanted) } },
            cancellationToken,
            clientId);
        return UnitResult.Success<Error>();
    }

    private async Task<Result<bool, Error>> AnyPrivilegedAsync(string clientId, IEnumerable<string> grant, CancellationToken cancellationToken)
    {
        foreach (string role in grant)
        {
            Result<IReadOnlyList<string>, Error> composites = await identityAdmin.GetRoleCompositesAsync(clientId, role, cancellationToken);
            if (composites.IsFailure)
            {
                return composites.Error;
            }

            if (composites.Value.Intersect(_privileged, StringComparer.Ordinal).Any())
            {
                return true;
            }
        }

        return false;
    }

    private async Task<UnitResult<Error>> ApplyAsync(
        string clientId,
        IdentityUser user,
        string[] grant,
        string[] revoke,
        IReadOnlyList<string> modes,
        IReadOnlyList<string> defaults,
        CancellationToken cancellationToken)
    {
        UnitResult<Error> roles = await identityAdmin.SetClientRolesAsync(user.Id, clientId, grant, revoke, cancellationToken);
        if (roles.IsFailure)
        {
            return roles;
        }

        // Режима нет или он больше недоступен — роль по умолчанию, а без неё (в приложении нет ролей для всех) — первая роль.
        string? mode = ActiveMode(clientId, user);
        string? fallback = defaults.Count > 0 ? defaults[0] : modes.Count > 0 ? modes[0] : null;
        if (!string.Equals(mode, fallback, StringComparison.Ordinal) && (mode is null || !modes.Contains(mode, StringComparer.Ordinal)))
        {
            UnitResult<Error> reset = await identityAdmin.UpdateUserAsync(
                user.Id,
                new UserChanges(null, new Dictionary<string, string?>(StringComparer.Ordinal) { [ModeAttribute(clientId)] = fallback }),
                cancellationToken);
            if (reset.IsFailure)
            {
                return reset;
            }
        }

        // Сессии живы, но токены со старыми ролями сразу не принимаются: клиент обновит их без перелогина.
        return await accessChanges.UserChangedAsync(user.Id, cancellationToken);
    }

    // Режим в приложении сервиса — атрибут active_mode, в других приложениях — active_mode.{clientId}.
    private bool IsServiceApp(string clientId) => string.Equals(clientId, authentication.Value.Audience, StringComparison.Ordinal);

    private string ModeAttribute(string clientId) => IsServiceApp(clientId) ? AuthClaims.ACTIVE_MODE : UserAttributes.AppModeOf(clientId);

    private string? ActiveMode(string clientId, IdentityUser user) =>
        IsServiceApp(clientId) ? user.ActiveMode : user.AppModes?.GetValueOrDefault(clientId);
}
