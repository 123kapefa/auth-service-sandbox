using AuthService.Contracts.Auth;
using AuthService.Core.Features.Auth.Cookies;
using AuthService.Core.Features.Auth.ReturnUrls;
using AuthService.Core.Features.Phone;
using AuthService.Core.Features.Security;
using AuthService.Core.Features.SignInMethods;
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
/// Браузер, с которого вошли, записывается к сессии — для раздела «Устройства» кабинета. Вернулись после смены номера —
/// запись в журнал; после удаления своих учётных данных — снять выключенные способы входа, которые без них не действуют.
/// Вход, который сервис не узнал (метка входа истекла через 10 минут, вход начат во второй вкладке, открыта старая
/// страница Keycloak), — не ошибка для человека: Keycloak его уже впустил, поэтому браузер уходит на страницу по
/// умолчанию, а оттуда без сессии — на вход, который Keycloak пропустит без ввода.
/// </summary>
public sealed class CallbackEndpoint : IEndpoint
{
    private const string UPDATE_PHONE = "update-phone";
    private const string DELETE_CREDENTIAL = "delete-credential";

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
        [FromServices] LoginDeviceRecorder devices,
        [FromServices] PhoneChangeRecorder phoneChanges,
        [FromServices] SignInMethodsHandler signInMethods,
        [FromServices] ReturnUrlValidator returnUrls,
        CancellationToken cancellationToken)
    {
        LoginState? saved = loginState.Take(httpContext.Request, httpContext.Response);

        Result<LoginCompleted, Error> completed = await handler.Handle(
            new CallbackRequest(code, state, error, errorDescription),
            saved,
            actionStatus,
            CallbackUrl.For(httpContext.Request),
            cancellationToken);
        if (completed.IsFailure && AuthErrors.IsLostLogin(completed.Error))
        {
            return Results.Redirect(returnUrls.Resolve(null));
        }

        if (completed.IsFailure)
        {
            return new EndpointResult<LoginCompleted>(completed);
        }

        await devices.RecordAsync(completed.Value.Tokens.AccessToken, httpContext.Request.Headers.UserAgent.ToString(), cancellationToken);
        if (string.Equals(saved?.Action, UPDATE_PHONE, StringComparison.Ordinal) && string.Equals(actionStatus, "success", StringComparison.Ordinal))
        {
            await phoneChanges.RecordAsync(completed.Value.Tokens.AccessToken, cancellationToken);
        }

        if (string.Equals(saved?.Action, DELETE_CREDENTIAL, StringComparison.Ordinal) && string.Equals(actionStatus, "success", StringComparison.Ordinal))
        {
            await signInMethods.ForgetSuspendedAfterLoginAsync(completed.Value.Tokens.AccessToken, cancellationToken);
        }

        cookies.SetTokens(httpContext.Response, completed.Value.Tokens);
        return Results.Redirect(completed.Value.ReturnTo);
    }
}
