using AuthService.Contracts.Auth;
using AuthService.Core.Features.Auth.Cookies;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Auth.UseCases.SwitchMode;

/// <summary>
/// POST /auth/mode { activeMode } — переключить активный режим. Режим недоступен → 403.
/// С refresh-cookie (BFF) — сразу новые токены в cookie; без неё клиент обновит токен сам.
/// </summary>
public sealed class SwitchModeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/mode", Handle).RequireUser();

    private static async Task<EndpointResult<SwitchModeResponse>> Handle(
        [FromBody] SwitchModeRequest request,
        HttpContext httpContext,
        [FromServices] CurrentUser user,
        [FromServices] SwitchModeHandler handler,
        [FromServices] AuthCookieWriter cookies,
        CancellationToken cancellationToken)
    {
        Result<ModeSwitched, Error> switched = await handler.Handle(
            user.Id,
            request,
            AuthCookieWriter.ReadRefreshToken(httpContext.Request),
            cancellationToken);
        if (switched.IsFailure)
        {
            if (switched.Error.Type == ErrorType.AUTHENTICATION)
            {
                cookies.Clear(httpContext.Response); // сессия закончилась или пользователь заблокирован
            }

            return Result.Failure<SwitchModeResponse, Error>(switched.Error);
        }

        if (switched.Value.Tokens is not null)
        {
            cookies.SetTokens(httpContext.Response, switched.Value.Tokens);
        }

        return Result.Success<SwitchModeResponse, Error>(switched.Value.Response);
    }
}
