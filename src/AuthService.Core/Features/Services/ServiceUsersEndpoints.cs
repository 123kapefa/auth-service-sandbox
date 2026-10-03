using AuthService.Contracts.Services;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Services;

/// <summary>
/// API для сервисов проекта — только по ключу доступа (токен сервиса), человеку → 403:
/// GET /auth/services/users/{userId} и GET /auth/services/users?login= — карточка (service.users.read);
/// GET /auth/services/users/{userId}/access?app=&amp;permission= — есть ли право сейчас (service.access.check).
/// </summary>
public sealed class ServiceUsersEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder users = app.MapGroup("/auth/services/users");
        users.MapGet("/{userId:guid}", GetAsync).RequireService(AuthServicePermissions.ServiceAccess.USERS_READ);
        users.MapGet(string.Empty, FindAsync).RequireService(AuthServicePermissions.ServiceAccess.USERS_READ);
        users.MapGet("/{userId:guid}/access", CheckAsync).RequireService(AuthServicePermissions.ServiceAccess.ACCESS_CHECK);
    }

    private static async Task<EndpointResult<ServiceUserResponse>> GetAsync(
        Guid userId, [FromServices] ServiceUsersHandler handler, CancellationToken cancellationToken)
    {
        Result<ServiceUserResponse, Error> result = await handler.GetAsync(userId, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<ServiceUserResponse>> FindAsync(
        [FromQuery] string? login, [FromServices] ServiceUsersHandler handler, CancellationToken cancellationToken)
    {
        Result<ServiceUserResponse, Error> result = await handler.FindAsync(login, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<AccessCheckResponse>> CheckAsync(
        Guid userId,
        [FromQuery] string? app,
        [FromQuery] string? permission,
        [FromServices] ServiceUsersHandler handler,
        CancellationToken cancellationToken)
    {
        Result<AccessCheckResponse, Error> result = await handler.CheckAsync(userId, app, permission, cancellationToken);
        return result;
    }
}
