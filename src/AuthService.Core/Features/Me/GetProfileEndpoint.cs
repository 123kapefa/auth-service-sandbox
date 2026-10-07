using AuthService.Contracts.Me;
using AuthService.Core.Abstractions;
using AuthService.Core.Features.Auth;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Me;

/// <summary>
/// GET /me/profile — профиль для личного кабинета: телефон и почта с сервера входа (в токене почты нет, а после смены
/// почты токен устарел бы). Любой вошедший — только свой.
/// </summary>
public sealed class GetProfileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/me/profile", HandleAsync).RequireUser();

    private static async Task<EndpointResult<ProfileResponse>> HandleAsync(
        [FromServices] CurrentUser user,
        [FromServices] IIdentityAdmin identityAdmin,
        CancellationToken cancellationToken)
    {
        if (user.Id is not { } id)
        {
            return Result.Failure<ProfileResponse, Error>(AuthErrors.SessionMissing());
        }

        Result<IdentityUser, Error> found = await identityAdmin.GetUserAsync(id, cancellationToken);
        return found.Map(account => new ProfileResponse(account.Id, account.Phone, account.Email, account.EmailVerified, account.CreatedAt, account.PhoneChangedAt));
    }
}
