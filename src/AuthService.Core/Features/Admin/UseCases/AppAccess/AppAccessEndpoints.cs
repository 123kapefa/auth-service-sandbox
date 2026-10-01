using AuthService.Contracts.Admin;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin.UseCases.AppAccess;

/// <summary>
/// GET /auth/admin/users/{userId}/apps — доступ к приложениям (право users.view);
/// PUT /auth/admin/users/{userId}/apps/{clientId}/access { hasAccess } — открыть или закрыть (право users.block).
/// </summary>
public sealed class AppAccessEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/admin/users/{userId:guid}/apps", GetAsync)
            .RequirePermissions(AuthServicePermissions.Users.VIEW);
        app.MapPut("/auth/admin/users/{userId:guid}/apps/{clientId}/access", SetAsync)
            .RequirePermissions(AuthServicePermissions.Users.BLOCK);
    }

    private static async Task<EndpointResult<IReadOnlyList<AppAccessResponse>>> GetAsync(
        Guid userId,
        [FromServices] CurrentUser caller,
        [FromServices] AppAccessHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AppAccessResponse>, Error> result =
            await handler.GetAsync(userId, AdminScope.OnlyCustomers(caller), cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<IReadOnlyList<AppAccessResponse>>> SetAsync(
        Guid userId,
        string clientId,
        [FromBody] SetAppAccessRequest request,
        [FromServices] CurrentUser caller,
        [FromServices] AppAccessHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AppAccessResponse>, Error> result = await handler.SetAsync(
            caller.Id,
            userId,
            clientId,
            request,
            AdminScope.OnlyCustomers(caller),
            cancellationToken);
        return result;
    }
}
