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

namespace AuthService.Core.Features.Admin.UseCases.BrandingSettings;

/// <summary>
/// Оформление проекта — право branding.manage (админ проекта): GET /auth/admin/branding — какое сейчас,
/// PUT /auth/admin/branding — сохранить (название, логотип, цвет, тема страниц входа).
/// </summary>
public sealed class BrandingEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder branding = app.MapGroup("/auth/admin/branding")
            .RequireUser()
            .RequirePermissions(AuthServicePermissions.Branding.MANAGE);
        branding.MapGet(string.Empty, GetAsync);
        branding.MapPut(string.Empty, SetAsync);
    }

    private static async Task<EndpointResult<BrandingResponse>> GetAsync(
        [FromServices] BrandingHandler handler, CancellationToken cancellationToken)
    {
        Result<BrandingResponse, Error> result = await handler.GetAsync(cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<BrandingResponse>> SetAsync(
        [FromBody] SetBrandingRequest request, [FromServices] BrandingHandler handler, CancellationToken cancellationToken)
    {
        Result<BrandingResponse, Error> result = await handler.SetAsync(request, cancellationToken);
        return result;
    }
}
