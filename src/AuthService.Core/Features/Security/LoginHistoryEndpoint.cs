using AuthService.Contracts.Security;
using AuthService.Core.Features.Auth;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Security;

/// <summary>GET /security/logins — свои входы и ошибки входа за 30 дней: когда, с какого IP и устройства. Любой вошедший.</summary>
public sealed class LoginHistoryEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/security/logins", HandleAsync).RequireUser();

    private static async Task<EndpointResult<LoginHistoryResponse>> HandleAsync(
        [FromServices] CurrentUser user,
        [FromServices] LoginHistoryHandler handler,
        CancellationToken cancellationToken) =>
        user.Id is { } id
            ? await handler.GetAsync(id, cancellationToken)
            : Result.Failure<LoginHistoryResponse, Error>(AuthErrors.SessionMissing());
}
