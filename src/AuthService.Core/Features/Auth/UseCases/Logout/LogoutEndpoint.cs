using AuthService.Core.Features.Auth.Cookies;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SharedKernel;
using PlatformAuth;

namespace AuthService.Core.Features.Auth.UseCases.Logout;

/// <summary>POST /auth/logout — завершить сессию в Keycloak и удалить cookie (cookie удаляются в любом случае).</summary>
public sealed class LogoutEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app.MapPost("/auth/logout", Handle);

    private static async Task<EndpointResult> Handle(
        HttpContext httpContext,
        [FromServices] LogoutHandler handler,
        [FromServices] AuthCookieWriter cookies,
        [FromServices] CurrentUser user,
        CancellationToken cancellationToken)
    {
        UnitResult<Error> logout;
        try
        {
            logout = await handler.Handle(cookies.ReadRefreshToken(httpContext.Request), user.SessionId, cancellationToken);
        }
        catch (Exception)
        {
            AuthMetrics.LoggedOut(success: false); // например, Redis недоступен: отметку отзыва не записали
            throw;
        }

        AuthMetrics.LoggedOut(logout.IsSuccess);
        cookies.Clear(httpContext.Response);
        return logout;
    }
}
