using AuthService.Contracts.Me;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.Core.Features.Admin.UseCases.AppUserRoles;
using AuthService.Core.Features.SignInMethods;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin.UseCases.UserSignInMethods;

/// <summary>
/// GET /auth/admin/users/{userId}/sign-in-methods — способы входа пользователя (право users.view; менеджеру — только
/// покупатели); POST /auth/admin/users/{userId}/sign-in-methods/reset — вернуть способы по умолчанию (право
/// users.credentials.manage — уровень аккаунта, только админ проекта; после недавнего входа). Выключать способы за
/// человека админ не может.
/// </summary>
public sealed class UserSignInMethodsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/admin/users/{userId:guid}/sign-in-methods", GetAsync)
            .RequirePermissions(AuthServicePermissions.Users.VIEW);
        app.MapPost("/auth/admin/users/{userId:guid}/sign-in-methods/reset", ResetAsync)
            .RequirePermissions(AuthServicePermissions.Users.MANAGE_CREDENTIALS);
    }

    private static async Task<EndpointResult<SignInMethodsResponse>> GetAsync(
        Guid userId,
        [FromServices] CurrentUser caller,
        [FromServices] AdminTargets targets,
        [FromServices] SignInMethodsHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IdentityUser, Error> user = await targets.FindAsync(userId, AdminScope.OnlyCustomers(caller), cancellationToken);
        return user.IsFailure
            ? Result.Failure<SignInMethodsResponse, Error>(user.Error)
            : await handler.GetAsync(userId, cancellationToken);
    }

    private static async Task<EndpointResult<SignInMethodsResponse>> ResetAsync(
        Guid userId,
        [FromServices] CurrentUser caller,
        [FromServices] SignInMethodsHandler handler,
        CancellationToken cancellationToken)
    {
        Result<SignInMethodsResponse, Error> result =
            await handler.ResetAsync(new RoleChangeActor(caller.Id, caller.AuthenticatedAt), userId, cancellationToken);
        return result;
    }
}
