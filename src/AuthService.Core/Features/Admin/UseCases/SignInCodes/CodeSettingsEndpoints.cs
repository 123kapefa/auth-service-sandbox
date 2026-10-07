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

namespace AuthService.Core.Features.Admin.UseCases.SignInCodes;

/// <summary>
/// Коды подтверждения проекта — право sign-in.manage (админ проекта): GET/PUT /auth/admin/codes/settings — способы
/// отправки на телефон, страны, бюджет, лимиты; GET /auth/admin/codes/usage — расход за сегодня.
/// </summary>
public sealed class CodeSettingsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder codes = app.MapGroup("/auth/admin/codes")
            .RequireUser()
            .RequirePermissions(AuthServicePermissions.SignIn.MANAGE);
        codes.MapGet("/settings", GetAsync);
        codes.MapPut("/settings", SetAsync);
        codes.MapGet("/usage", UsageAsync);
    }

    private static async Task<EndpointResult<CodeSettingsResponse>> GetAsync(
        [FromServices] CodeSettingsHandler handler, CancellationToken cancellationToken)
    {
        Result<CodeSettingsResponse, Error> result = await handler.GetAsync(cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<CodeSettingsResponse>> SetAsync(
        [FromBody] SetCodeSettingsRequest request, [FromServices] CodeSettingsHandler handler, CancellationToken cancellationToken)
    {
        Result<CodeSettingsResponse, Error> result = await handler.SetAsync(request, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<CodeUsageResponse>> UsageAsync(
        [FromServices] CodeSettingsHandler handler, CancellationToken cancellationToken)
    {
        Result<CodeUsageResponse, Error> result = await handler.UsageAsync(cancellationToken);
        return result;
    }
}
