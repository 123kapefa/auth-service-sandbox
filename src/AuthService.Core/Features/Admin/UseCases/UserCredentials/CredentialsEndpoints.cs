using AuthService.Contracts.Admin;
using AuthService.Core.Authorization;
using AuthService.Core.Features.Admin.UseCases.AppUserRoles;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin.UseCases.UserCredentials;

/// <summary>
/// POST /auth/admin/users/{userId}/password-reset — письмо со ссылкой на смену пароля;
/// DELETE /auth/admin/users/{userId}/two-factor — отключить 2FA (после недавнего входа). Право users.credentials.manage —
/// уровень аккаунта, только админ проекта. Ответ — карточка пользователя.
/// </summary>
public sealed class CredentialsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/admin/users/{userId:guid}/password-reset", ResetAsync)
            .RequirePermissions(AuthServicePermissions.Users.MANAGE_CREDENTIALS);
        app.MapDelete("/auth/admin/users/{userId:guid}/two-factor", DisableTwoFactorAsync)
            .RequirePermissions(AuthServicePermissions.Users.MANAGE_CREDENTIALS);
    }

    private static async Task<EndpointResult<UserDetailResponse>> ResetAsync(
        Guid userId, [FromServices] CurrentUser caller, [FromServices] CredentialsHandler handler, CancellationToken cancellationToken)
    {
        Result<UserDetailResponse, Error> result =
            await handler.SendPasswordResetAsync(new RoleChangeActor(caller.Id, caller.AuthenticatedAt), userId, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<UserDetailResponse>> DisableTwoFactorAsync(
        Guid userId, [FromServices] CurrentUser caller, [FromServices] CredentialsHandler handler, CancellationToken cancellationToken)
    {
        Result<UserDetailResponse, Error> result =
            await handler.DisableTwoFactorAsync(new RoleChangeActor(caller.Id, caller.AuthenticatedAt), userId, cancellationToken);
        return result;
    }
}
