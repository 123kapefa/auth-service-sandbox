using AuthService.Contracts.Auth;
using AuthService.Core.Features.Auth.Cookies;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SharedKernel;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace AuthService.Core.Features.Auth.UseCases.Callback;

/// <summary>
/// GET /auth/callback?code&amp;state — возврат со страницы входа: cookie с токенами и редирект на return_to.
/// kc_action_status — итог действия с учётной записью (смена пароля, почты): уходит в return_to как action_status.
/// </summary>
public sealed class CallbackEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app.MapGet(CallbackUrl.PATH, Handle);

    private static async Task<IResult> Handle(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        [FromQuery(Name = "kc_action_status")] string? actionStatus,
        HttpContext httpContext,
        [FromServices] CallbackHandler handler,
        [FromServices] LoginStateStore loginState,
        [FromServices] AuthCookieWriter cookies,
        CancellationToken cancellationToken)
    {
        LoginState? saved = loginState.Take(httpContext.Request, httpContext.Response);

        Result<LoginCompleted, Error> completed = await handler.Handle(
            new CallbackRequest(code, state, error, errorDescription),
            saved,
            actionStatus,
            CallbackUrl.For(httpContext.Request),
            cancellationToken);
        if (completed.IsFailure)
        {
            return new EndpointResult<LoginCompleted>(completed);
        }

        cookies.SetTokens(httpContext.Response, completed.Value.Tokens);
        return Results.Redirect(completed.Value.ReturnTo);
    }
}
