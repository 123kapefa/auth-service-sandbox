using AuthService.Core.Features.Auth.Cookies;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SharedKernel;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace AuthService.Core.Features.Auth.UseCases.Login;

/// <summary>GET /auth/login?return_to=… — уход на страницу входа Keycloak.</summary>
public sealed class LoginEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app.MapGet("/auth/login", Handle);

    private static async Task<IResult> Handle(
        [FromQuery(Name = "return_to")] string? returnTo,
        HttpContext httpContext,
        [FromServices] LoginHandler handler,
        [FromServices] LoginStateStore loginState,
        CancellationToken cancellationToken)
    {
        Result<LoginStart, Error> start = await handler.Handle(returnTo, CallbackUrl.For(httpContext.Request), cancellationToken);
        if (start.IsFailure)
        {
            return new EndpointResult<LoginStart>(start);
        }

        loginState.Save(httpContext.Response, start.Value.State);
        return Results.Redirect(start.Value.RedirectTo.ToString());
    }
}
