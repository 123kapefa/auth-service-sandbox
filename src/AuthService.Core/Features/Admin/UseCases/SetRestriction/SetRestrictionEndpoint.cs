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

namespace AuthService.Core.Features.Admin.UseCases.SetRestriction;

/// <summary>PUT /auth/admin/users/{userId}/restriction { restricted, reason } — блокировка аккаунта; право users.block.</summary>
public sealed class SetRestrictionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/auth/admin/users/{userId:guid}/restriction", Handle).RequirePermissions(AuthServicePermissions.Users.BLOCK);

    private static async Task<EndpointResult<UserDetailResponse>> Handle(
        Guid userId,
        [FromBody] SetRestrictionRequest request,
        [FromServices] CurrentUser caller,
        [FromServices] SetRestrictionHandler handler,
        CancellationToken cancellationToken)
    {
        Result<UserDetailResponse, Error> result = await handler.Handle(
            caller.Id,
            userId,
            request,
            AdminScope.OnlyCustomers(caller),
            cancellationToken);
        return result;
    }
}
