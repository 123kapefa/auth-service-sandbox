using AuthService.Contracts.Admin;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin.UseCases.ServiceKeys;

/// <summary>
/// Сервисы проекта и ключи доступа — право services.manage (админ проекта): GET/POST /auth/admin/services,
/// GET /auth/admin/services/grantable — права в приложениях проекта, которые можно выдать ключу,
/// POST /auth/admin/services/{code}/rotate { keepTokens } — перевыпустить (keepTokens — плановая смена без остановки), PUT /auth/admin/services/{code}/permissions — права сервиса,
/// DELETE /auth/admin/services/{code} — отозвать.
/// Секрет — только в ответе на выдачу и перевыпуск.
/// </summary>
public sealed class ServiceKeysEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Только человек (защита в глубину: даже если сервису вручную дадут services.manage, ключами он не управляет).
        RouteGroupBuilder services = app.MapGroup("/auth/admin/services")
            .RequireUser()
            .RequirePermissions(AuthServicePermissions.ProjectServices.MANAGE);
        services.MapGet(string.Empty, ListAsync);
        services.MapGet("/grantable", GrantableAsync);
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

    private static async Task<EndpointResult<IReadOnlyList<GrantableAppPermissionsResponse>>> GrantableAsync(
        [FromServices] ServiceKeysHandler handler, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<GrantableAppPermissionsResponse>, Error> result = await handler.GrantableAsync(cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<IssuedServiceKeyResponse>> IssueAsync(
        [FromBody] IssueServiceKeyRequest request, [FromServices] ServiceKeysHandler handler, CancellationToken cancellationToken)
    {
        Result<IssuedServiceKeyResponse, Error> result = await handler.IssueAsync(request, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<IssuedServiceKeyResponse>> RotateAsync(
        string code,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RotateServiceKeyRequest? request,
        [FromServices] ServiceKeysHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IssuedServiceKeyResponse, Error> result = await handler.RotateAsync(code, request?.KeepTokens == true, cancellationToken);
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
