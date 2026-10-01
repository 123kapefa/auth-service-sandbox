using AuthService.Contracts.Security;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.Core.Features.Security;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin.UseCases.UserSessions;

/// <summary>
/// GET /auth/admin/users/{userId}/sessions — сессии пользователя (право users.view; менеджеру — только покупатели);
/// POST /auth/admin/users/{userId}/logout — «выйти везде»: все сессии во всех приложениях (право users.sessions.manage —
/// уровень аккаунта, только админ проекта).
/// </summary>
public sealed class UserSessionsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/admin/users/{userId:guid}/sessions", ListAsync)
            .RequirePermissions(AuthServicePermissions.Users.VIEW);
        app.MapPost("/auth/admin/users/{userId:guid}/logout", LogoutAsync)
            .RequirePermissions(AuthServicePermissions.Users.MANAGE_SESSIONS);
    }

    private static async Task<EndpointResult<SessionsResponse>> ListAsync(
        Guid userId,
        [FromServices] CurrentUser caller,
        [FromServices] AdminTargets targets,
        [FromServices] SessionsHandler sessions,
        CancellationToken cancellationToken)
    {
        Result<IdentityUser, Error> user = await targets.FindAsync(userId, AdminScope.OnlyCustomers(caller), cancellationToken);
        return user.IsFailure
            ? Result.Failure<SessionsResponse, Error>(user.Error)
            : await sessions.ListAsync(userId, currentSessionId: null, cancellationToken);
    }

    private static async Task<EndpointResult<SessionsResponse>> LogoutAsync(
        Guid userId,
        [FromServices] AdminTargets targets,
        [FromServices] IIdentityAdmin identityAdmin,
        [FromServices] SessionsHandler sessions,
        CancellationToken cancellationToken)
    {
        Result<IdentityUser, Error> user = await targets.FindAsync(userId, onlyCustomers: false, cancellationToken);
        if (user.IsFailure)
        {
            return Result.Failure<SessionsResponse, Error>(user.Error);
        }

        UnitResult<Error> loggedOut = await identityAdmin.LogoutUserAsync(userId, cancellationToken);
        return loggedOut.IsFailure
            ? Result.Failure<SessionsResponse, Error>(loggedOut.Error)
            : await sessions.ListAsync(userId, currentSessionId: null, cancellationToken);
    }
}
