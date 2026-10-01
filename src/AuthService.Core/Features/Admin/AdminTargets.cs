using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin;

/// <summary>С кем работает админка: пользователь существует (иначе 404), менеджеру — только покупатели (иначе 403).</summary>
public sealed class AdminTargets(IIdentityAdmin identityAdmin, IOptions<JwtAuthenticationOptions> authentication)
{
    public async Task<Result<IdentityUser, Error>> FindAsync(Guid userId, bool onlyCustomers, CancellationToken cancellationToken)
    {
        Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(userId, cancellationToken);
        if (user.IsFailure || !onlyCustomers)
        {
            return user;
        }

        Result<IReadOnlyList<string>, Error> roles =
            await identityAdmin.GetClientRolesAsync(userId, authentication.Value.Audience, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        return StaffRoles.Of(roles.Value).Count > 0 ? AdminErrors.StaffNotAllowed() : user;
    }
}
