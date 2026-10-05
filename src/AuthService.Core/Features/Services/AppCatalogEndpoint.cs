using AuthService.Contracts.Services;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Services;

/// <summary>
/// PUT /auth/services/catalog — сервис приложения объявляет права и встроенные роли своего приложения (из кода, при
/// старте). Только по ключу с правом service.catalog.declare; приложение — по коду ключа. Правила — AppCatalogHandler.
/// </summary>
public sealed class AppCatalogEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/auth/services/catalog", Handle).RequireService(AuthServicePermissions.ServiceAccess.CATALOG_DECLARE);

    private static async Task<EndpointResult<DeclareCatalogResponse>> Handle(
        [FromBody] DeclareCatalogRequest request,
        [FromServices] CurrentUser caller,
        [FromServices] AppCatalogHandler handler,
        CancellationToken cancellationToken)
    {
        Result<DeclareCatalogResponse, Error> result = await handler.DeclareAsync(caller.ServiceClientId, request, cancellationToken);
        return result;
    }
}
