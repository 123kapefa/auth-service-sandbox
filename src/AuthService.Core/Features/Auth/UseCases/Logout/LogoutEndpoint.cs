using AuthService.Core.Features.Auth.Cookies;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace AuthService.Core.Features.Auth.UseCases.Logout;

/// <summary>POST /auth/logout — завершить сессию в Keycloak и удалить cookie (cookie удаляются в любом случае).</summary>
public sealed class LogoutEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app.MapPost("/auth/logout", Handle);

    private static async Task<EndpointResult> Handle(
        HttpContext httpContext,
        [FromServices] LogoutHandler handler,
        [FromServices] AuthCookieWriter cookies,
        CancellationToken cancellationToken)
    {
        UnitResult<Error> logout = await handler.Handle(AuthCookieWriter.ReadRefreshToken(httpContext.Request), cancellationToken);
        cookies.Clear(httpContext.Response);
        return logout;
    }
}
