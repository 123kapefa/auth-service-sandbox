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

namespace AuthService.Core.Features.Admin.UseCases.AppUserRoles;

/// <summary>GET/PUT /auth/admin/apps/{clientId}/users/{userId}/roles — роли пользователя в приложении; право users.roles.manage.</summary>
public sealed class UserRolesEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder roles = app.MapGroup("/auth/admin/apps/{clientId}/users/{userId:guid}/roles")
            .RequireAppPermission(AuthServicePermissions.Users.MANAGE_ROLES);
        roles.MapGet(string.Empty, GetAsync);
        roles.MapPut(string.Empty, SetAsync);
    }

    private static async Task<EndpointResult<UserRolesResponse>> GetAsync(
        string clientId, Guid userId, [FromServices] UserRolesHandler handler, CancellationToken cancellationToken)
    {
        Result<UserRolesResponse, Error> result = await handler.GetAsync(clientId, userId, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<UserRolesResponse>> SetAsync(
        string clientId,
        Guid userId,
        [FromBody] SetUserRolesRequest request,
        [FromServices] CurrentUser caller,
        [FromServices] UserRolesHandler handler,
        CancellationToken cancellationToken)
    {
        Result<UserRolesResponse, Error> result = await handler.SetAsync(
            new RoleChangeActor(caller.Id, caller.AuthenticatedAt), clientId, userId, request, cancellationToken);
        return result;
    }
}
