using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Me;

/// <summary>
/// GET /me/branding?app=… — оформление для личного кабинета: проекта, поверх — приложения, из которого кабинет открыт
/// (название, логотип, цвет). Любой вошедший: то же видно на страницах входа без входа.
/// </summary>
public sealed class GetBrandingEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/me/branding", HandleAsync).RequireUser();

    private static async Task<EndpointResult<BrandingResponse>> HandleAsync(
        [FromQuery(Name = "app")] string? from,
        [FromServices] IProjectBranding branding,
        CancellationToken cancellationToken)
    {
        Result<ProjectBranding, Error> result = await branding.GetEffectiveAsync(BrandingApps.Normalize(from), cancellationToken);
        return result.Map(effective => new BrandingResponse(effective.Name, effective.Logo, effective.Color, effective.Theme, effective.ColorDark));
    }
}
