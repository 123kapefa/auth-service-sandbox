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

namespace AuthService.Core.Features.Admin.UseCases.ServiceKeys;

/// <summary>
/// Сервисы проекта и ключи доступа — право services.manage (админ проекта): GET/POST /auth/admin/services,
/// POST /auth/admin/services/{code}/rotate — перевыпустить, PUT /auth/admin/services/{code}/permissions — права сервиса,
/// DELETE /auth/admin/services/{code} — отозвать.
/// Секрет — только в ответе на выдачу и перевыпуск.
/// </summary>
public sealed class ServiceKeysEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder services = app.MapGroup("/auth/admin/services")
            .RequirePermissions(AuthServicePermissions.ProjectServices.MANAGE);
        services.MapGet(string.Empty, ListAsync);
        services.MapPost(string.Empty, IssueAsync);
        services.MapPost("/{code}/rotate", RotateAsync);
        services.MapPut("/{code}/permissions", SetPermissionsAsync);
        services.MapDelete("/{code}", RevokeAsync);
    }

    private static async Task<EndpointResult<IReadOnlyList<ServiceKeyResponse>>> ListAsync(
        [FromServices] ServiceKeysHandler handler, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ServiceKeyResponse>, Error> result = await handler.ListAsync(cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<IssuedServiceKeyResponse>> IssueAsync(
        [FromBody] IssueServiceKeyRequest request, [FromServices] ServiceKeysHandler handler, CancellationToken cancellationToken)
    {
        Result<IssuedServiceKeyResponse, Error> result = await handler.IssueAsync(request, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<IssuedServiceKeyResponse>> RotateAsync(
        string code, [FromServices] ServiceKeysHandler handler, CancellationToken cancellationToken)
    {
        Result<IssuedServiceKeyResponse, Error> result = await handler.RotateAsync(code, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<ServiceKeyResponse>> SetPermissionsAsync(
        string code, [FromBody] SetServicePermissionsRequest request, [FromServices] ServiceKeysHandler handler, CancellationToken cancellationToken)
    {
        Result<ServiceKeyResponse, Error> result = await handler.SetPermissionsAsync(code, request, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult> RevokeAsync(
        string code, [FromServices] ServiceKeysHandler handler, CancellationToken cancellationToken)
    {
        UnitResult<Error> result = await handler.RevokeAsync(code, cancellationToken);
        return result;
    }
}
