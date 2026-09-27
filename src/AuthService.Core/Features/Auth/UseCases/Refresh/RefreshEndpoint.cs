using AuthService.Contracts.Auth;
using AuthService.Core.Abstractions;
using AuthService.Core.Features.Auth.Cookies;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace AuthService.Core.Features.Auth.UseCases.Refresh;

/// <summary>POST /auth/refresh — новые токены в cookie. Refresh не принят → cookie удаляются, 401.</summary>
public sealed class RefreshEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app.MapPost("/auth/refresh", Handle);

    private static async Task<EndpointResult<RefreshResponse>> Handle(
        HttpContext httpContext,
        [FromServices] RefreshHandler handler,
        [FromServices] AuthCookieWriter cookies,
        CancellationToken cancellationToken)
    {
        Result<TokenSet, Error> tokens = await handler.Handle(AuthCookieWriter.ReadRefreshToken(httpContext.Request), cancellationToken);
        if (tokens.IsFailure)
        {
            if (tokens.Error.Type == ErrorType.AUTHENTICATION)
            {
                cookies.Clear(httpContext.Response); // сессия закончилась — не оставляем мёртвые cookie
            }

            return Result.Failure<RefreshResponse, Error>(tokens.Error);
        }

        cookies.SetTokens(httpContext.Response, tokens.Value);
        return Result.Success<RefreshResponse, Error>(new RefreshResponse(tokens.Value.AccessTokenExpiresAt));
    }
}
