using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin;

/// <summary>С кем работает админка: пользователь существует (иначе 404), менеджеру — только покупатели (иначе 403).</summary>
public sealed class AdminTargets(IIdentityAdmin identityAdmin, UserAppRoles userRoles, IOptions<JwtAuthenticationOptions> authentication)
{
    public async Task<Result<IdentityUser, Error>> FindAsync(Guid userId, bool onlyCustomers, CancellationToken cancellationToken)
    {
        Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(userId, cancellationToken);
        if (user.IsFailure || !onlyCustomers)
        {
            return user;
        }

        Result<UserRoles, Error> roles = await userRoles.LoadAsync(userId, authentication.Value.Audience, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        return roles.Value.Assigned.Count > 0 ? AdminErrors.StaffNotAllowed() : user;
    }
}
