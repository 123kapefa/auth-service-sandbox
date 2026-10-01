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

namespace AuthService.Core.Features.Admin.UseCases.RoleCatalog;

/// <summary>
/// GET/POST /auth/admin/apps/{clientId}/roles, PUT/DELETE …/roles/{name} — роли приложения и их права;
/// право roles.manage. Админ приложения (только своё приложение) — шаг 4а.7.
/// </summary>
public sealed class RolesEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder roles = app.MapGroup("/auth/admin/apps/{clientId}/roles")
            .RequirePermissions(AuthServicePermissions.Roles.MANAGE);
        roles.MapGet(string.Empty, ListAsync);
        roles.MapPost(string.Empty, CreateAsync);
        roles.MapPut("/{name}", UpdateAsync);
        roles.MapDelete("/{name}", DeleteAsync);
    }

    private static async Task<EndpointResult<IReadOnlyList<RoleResponse>>> ListAsync(
        string clientId, [FromServices] RolesHandler handler, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RoleResponse>, Error> result = await handler.ListAsync(clientId, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<IReadOnlyList<RoleResponse>>> CreateAsync(
        string clientId, [FromBody] CreateRoleRequest request, [FromServices] RolesHandler handler, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RoleResponse>, Error> result = await handler.CreateAsync(clientId, request, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<IReadOnlyList<RoleResponse>>> UpdateAsync(
        string clientId, string name, [FromBody] UpdateRoleRequest request, [FromServices] RolesHandler handler, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RoleResponse>, Error> result = await handler.UpdateAsync(clientId, name, request, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<IReadOnlyList<RoleResponse>>> DeleteAsync(
        string clientId, string name, [FromServices] RolesHandler handler, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RoleResponse>, Error> result = await handler.DeleteAsync(clientId, name, cancellationToken);
        return result;
    }
}
