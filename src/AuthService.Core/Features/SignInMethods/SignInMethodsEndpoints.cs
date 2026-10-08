using AuthService.Contracts.Me;
using AuthService.Core.Features.Auth;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.SignInMethods;

/// <summary>
/// GET /me/sign-in-methods — свои способы входа: какие есть и включены, что можно выключить, история изменений;
/// PUT /me/sign-in-methods/{method} { enabled, signOutOtherDevices } — выключить или включить пароль (password), код на
/// телефон (phone-code) или код на почту (email-code);
/// DELETE /me/sign-in-methods/providers/{provider}?signOutOtherDevices=false — отвязать Яндекс ID (yandex) или Госуслуги
/// (esia). Менять — только после недавнего входа. Любой вошедший — только своё.
/// </summary>
public sealed class SignInMethodsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder methods = app.MapGroup("/me/sign-in-methods").RequireUser();
        methods.MapGet(string.Empty, GetAsync);
        methods.MapPut("/{method}", SetAsync);
        methods.MapDelete("/providers/{provider}", UnlinkAsync);
    }

    private static async Task<EndpointResult<SignInMethodsResponse>> GetAsync(
        [FromServices] CurrentUser user,
        [FromServices] SignInMethodsHandler handler,
        CancellationToken cancellationToken) =>
        user.Id is { } id
            ? await handler.GetAsync(id, cancellationToken)
            : Result.Failure<SignInMethodsResponse, Error>(AuthErrors.SessionMissing());

    private static async Task<EndpointResult<SignInMethodsResponse>> SetAsync(
        string method,
        [FromBody] SetSignInMethodRequest request,
        [FromServices] CurrentUser user,
        [FromServices] SignInMethodsHandler handler,
        CancellationToken cancellationToken) =>
        user.Id is { } id
            ? await handler.SetAsync(new SignInMethodsActor(id, user.AuthenticatedAt, user.SessionId), method, request, cancellationToken)
            : Result.Failure<SignInMethodsResponse, Error>(AuthErrors.SessionMissing());

    private static async Task<EndpointResult<SignInMethodsResponse>> UnlinkAsync(
        string provider,
        [FromQuery] bool? signOutOtherDevices,
        [FromServices] CurrentUser user,
        [FromServices] SignInMethodsHandler handler,
        CancellationToken cancellationToken) =>
        user.Id is { } id
            ? await handler.UnlinkAsync(new SignInMethodsActor(id, user.AuthenticatedAt, user.SessionId), provider, signOutOtherDevices, cancellationToken)
            : Result.Failure<SignInMethodsResponse, Error>(AuthErrors.SessionMissing());
}
