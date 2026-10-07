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
/// Способы входа проекта — право sign-in.manage (админ проекта): GET/PUT /auth/admin/sign-in/methods — что доступно
/// людям проекта, 2FA, переходный период; GET /auth/admin/sign-in/methods/{method}/affected — сколько людей входит
/// способом и сколько только им.
/// </summary>
public sealed class ProjectSignInEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder methods = app.MapGroup("/auth/admin/sign-in/methods")
            .RequireUser()
            .RequirePermissions(AuthServicePermissions.SignIn.MANAGE);
        methods.MapGet(string.Empty, GetAsync);
        methods.MapPut(string.Empty, SetAsync);
        methods.MapGet("/{method}/affected", AffectedAsync);
    }

    private static async Task<EndpointResult<SignInMethodsResponse>> GetAsync(
        [FromServices] ProjectSignInHandler handler, CancellationToken cancellationToken)
    {
        Result<SignInMethodsResponse, Error> result = await handler.GetAsync(cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<SignInMethodsResponse>> SetAsync(
        [FromBody] SetSignInMethodsRequest request, [FromServices] ProjectSignInHandler handler, CancellationToken cancellationToken)
    {
        Result<SignInMethodsResponse, Error> result = await handler.SetAsync(request, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<SignInMethodUsersResponse>> AffectedAsync(
        string method, [FromServices] ProjectSignInHandler handler, CancellationToken cancellationToken)
    {
        Result<SignInMethodUsersResponse, Error> result = await handler.AffectedAsync(method, cancellationToken);
        return result;
    }
}
