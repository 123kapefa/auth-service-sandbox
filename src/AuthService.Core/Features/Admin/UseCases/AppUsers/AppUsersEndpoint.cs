using AuthService.Contracts.Admin;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace AuthService.Core.Features.Admin.UseCases.AppUsers;

/// <summary>
/// GET /auth/admin/apps/{clientId}/users?search&amp;page&amp;pageSize — пользователи с ролью в приложении, search — точный
/// телефон или почта (найти человека, чтобы выдать роль); право
/// users.roles.manage (у админа проекта — в токене, у админа приложения — в самом приложении).
/// </summary>
public sealed class AppUsersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/auth/admin/apps/{clientId}/users", Handle).RequireAppPermission(AuthServicePermissions.Users.MANAGE_ROLES);

    private static async Task<EndpointResult<AppUsersPageResponse>> Handle(
        string clientId,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] AppUsersHandler handler,
        CancellationToken cancellationToken)
    {
        Result<AppUsersPageResponse, Error> result = await handler.Handle(clientId, new UsersQuery(search, page, pageSize), cancellationToken);
        return result;
    }
}
