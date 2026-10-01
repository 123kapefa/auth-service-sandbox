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

namespace AuthService.Core.Features.Admin.UseCases.Permissions;

/// <summary>
/// GET/POST /auth/admin/apps/{clientId}/permissions, DELETE …/permissions/{name} — права приложения;
/// право roles.manage. Админ приложения (только своё приложение) — шаг 4а.7.
/// </summary>
public sealed class PermissionsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder permissions = app.MapGroup("/auth/admin/apps/{clientId}/permissions")
            .RequirePermissions(AuthServicePermissions.Roles.MANAGE);
        permissions.MapGet(string.Empty, ListAsync);
        permissions.MapPost(string.Empty, CreateAsync);
        permissions.MapDelete("/{name}", DeleteAsync);
    }

    private static async Task<EndpointResult<IReadOnlyList<PermissionResponse>>> ListAsync(
        string clientId,
        [FromServices] PermissionsHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<PermissionResponse>, Error> result = await handler.ListAsync(clientId, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<IReadOnlyList<PermissionResponse>>> CreateAsync(
        string clientId,
        [FromBody] CreatePermissionRequest request,
        [FromServices] PermissionsHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<PermissionResponse>, Error> result = await handler.CreateAsync(clientId, request, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<IReadOnlyList<PermissionResponse>>> DeleteAsync(
        string clientId,
        string name,
        [FromServices] PermissionsHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<PermissionResponse>, Error> result = await handler.DeleteAsync(clientId, name, cancellationToken);
        return result;
    }
}
