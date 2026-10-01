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

namespace AuthService.Core.Features.Admin.UseCases.GetUser;

/// <summary>GET /auth/admin/users/{userId} — карточка пользователя; право users.view.</summary>
public sealed class GetUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/auth/admin/users/{userId:guid}", Handle).RequirePermissions(AuthServicePermissions.Users.VIEW);

    private static async Task<EndpointResult<UserDetailResponse>> Handle(
        Guid userId,
        [FromServices] CurrentUser caller,
        [FromServices] GetUserHandler handler,
        CancellationToken cancellationToken)
    {
        Result<UserDetailResponse, Error> result = await handler.Handle(userId, AdminScope.OnlyCustomers(caller), cancellationToken);
        return result;
    }
}
