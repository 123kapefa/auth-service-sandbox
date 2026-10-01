using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin.UseCases.GetUser;

/// <summary>Карточка пользователя. Нет такого — 404; менеджер и сотрудник — 403, как в боевом сервисе.</summary>
public sealed class GetUserHandler(IIdentityAdmin identityAdmin, UserAppRoles userRoles, IOptions<JwtAuthenticationOptions> authentication)
{
    public async Task<Result<UserDetailResponse, Error>> Handle(
        Guid userId,
        bool onlyCustomers,
        CancellationToken cancellationToken)
    {
        Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(userId, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        Result<UserRoles, Error> roles = await userRoles.LoadAsync(userId, authentication.Value.Audience, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        if (onlyCustomers && roles.Value.Assigned.Count > 0)
        {
            return AdminErrors.StaffNotAllowed();
        }

        Result<bool, Error> hasPassword = await identityAdmin.HasPasswordAsync(userId, cancellationToken);
        if (hasPassword.IsFailure)
        {
            return hasPassword.Error;
        }

        Result<IReadOnlyList<LoginEvent>, Error> lastLogin =
            await identityAdmin.GetLoginEventsAsync(userId, [LoginEventTypes.LOGIN], 0, 1, cancellationToken);
        if (lastLogin.IsFailure)
        {
            return lastLogin.Error;
        }

        return new UserDetailResponse(
            user.Value.Id,
            user.Value.Username,
            user.Value.Phone,
            user.Value.Email,
            StaffRoles.Describe(roles.Value.Assigned),
            roles.Value.Modes,
            user.Value.ActiveMode,
            user.Value.CreatedAt,
            !user.Value.Enabled,
            user.Value.RestrictionReason,
            user.Value.RestrictedAt,
            hasPassword.Value,
            lastLogin.Value.Count > 0 ? lastLogin.Value[0].Time : null);
    }
}
