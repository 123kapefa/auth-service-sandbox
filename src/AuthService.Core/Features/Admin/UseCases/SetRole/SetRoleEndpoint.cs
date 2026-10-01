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

namespace AuthService.Core.Features.Admin.UseCases.SetRole;

/// <summary>PUT /auth/admin/users/{userId}/role { role } — смена роли; право users.roles.manage (админ в режиме admin).</summary>
public sealed class SetRoleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/auth/admin/users/{userId:guid}/role", Handle).RequirePermissions(AuthServicePermissions.Users.MANAGE_ROLES);

    private static async Task<EndpointResult<UserDetailResponse>> Handle(
        Guid userId,
        [FromBody] SetRoleRequest request,
        [FromServices] CurrentUser caller,
        [FromServices] SetRoleHandler handler,
        CancellationToken cancellationToken)
    {
        Result<UserDetailResponse, Error> result = await handler.Handle(
            new RoleChangeActor(caller.Id, caller.AuthenticatedAt),
            userId,
            request,
            cancellationToken);
        return result;
    }
}
