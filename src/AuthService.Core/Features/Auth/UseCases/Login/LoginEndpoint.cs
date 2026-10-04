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

/// <summary>
/// GET /auth/login?return_to=…&amp;reauth=true&amp;action=… — уход на страницу входа Keycloak.
/// reauth=true — войти заново даже при живой сессии: перед действием, которому нужен недавний вход (выдача admin).
/// action=update-password|update-email|configure-totp|register-passkey|configure-recovery-codes|delete-credential
/// (с credential=Id) — после входа страница действия на сервере входа (итог — action_status в return_to).
/// app=clientId — вход в приложение: страницы входа и письма — в его оформлении (неверное значение не учитывается).
/// </summary>
public sealed class LoginEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app.MapGet("/auth/login", Handle);

    private static async Task<IResult> Handle(
        [FromQuery(Name = "return_to")] string? returnTo,
        [FromQuery] bool? reauth,
        [FromQuery] string? action,
        [FromQuery] string? credential,
        [FromQuery] string? app,
        HttpContext httpContext,
        [FromServices] LoginHandler handler,
        [FromServices] LoginStateStore loginState,
        CancellationToken cancellationToken)
    {
        Result<LoginStart, Error> start = await handler.Handle(returnTo, reauth == true, action, credential, app, CallbackUrl.For(httpContext.Request), cancellationToken);
        if (start.IsFailure)
        {
            return new EndpointResult<LoginStart>(start);
        }

        loginState.Save(httpContext.Response, start.Value.State);
        return Results.Redirect(start.Value.RedirectTo.ToString());
    }
}
