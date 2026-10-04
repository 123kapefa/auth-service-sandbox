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

namespace AuthService.Core.Features.Admin.UseCases.GetUsers;

/// <summary>GET /auth/admin/users?search&amp;page&amp;pageSize — пользователи; право users.view. Менеджер видит только покупателей.</summary>
public sealed class GetUsersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/auth/admin/users", Handle).RequirePermissions(AuthServicePermissions.Users.VIEW);

    private static async Task<EndpointResult<UsersPageResponse>> Handle(
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] CurrentUser caller,
        [FromServices] GetUsersHandler handler,
        CancellationToken cancellationToken)
    {
        Result<UsersPageResponse, Error> result = await handler.Handle(
            new UsersQuery(search, page, pageSize),
            AdminScope.OnlyCustomers(caller),
            cancellationToken);
        return result;
    }
}
