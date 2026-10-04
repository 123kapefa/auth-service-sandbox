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
/// Оформление — право branding.manage (админ проекта): GET/PUT /auth/admin/branding — проекта (название, логотип, цвет,
/// тема страниц входа); GET/PUT /auth/admin/apps/{clientId}/branding — приложения поверх проекта (название, логотип,
/// цвет; null — как у проекта).
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

        RouteGroupBuilder appBranding = app.MapGroup("/auth/admin/apps/{clientId}/branding")
            .RequireUser()
            .RequirePermissions(AuthServicePermissions.Branding.MANAGE);
        appBranding.MapGet(string.Empty, GetAppAsync);
        appBranding.MapPut(string.Empty, SetAppAsync);
    }

    private static async Task<EndpointResult<AppBrandingResponse>> GetAppAsync(
        string clientId, [FromServices] AppBrandingHandler handler, CancellationToken cancellationToken)
    {
        Result<AppBrandingResponse, Error> result = await handler.GetAsync(clientId, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<AppBrandingResponse>> SetAppAsync(
        string clientId, [FromBody] SetAppBrandingRequest request, [FromServices] AppBrandingHandler handler, CancellationToken cancellationToken)
    {
        Result<AppBrandingResponse, Error> result = await handler.SetAsync(clientId, request, cancellationToken);
        return result;
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
