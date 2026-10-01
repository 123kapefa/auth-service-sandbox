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

/// <summary>
/// Свои сессии, как в боевом сервисе: GET /security/sessions, POST /security/sessions/{id}/terminate,
/// POST /security/sessions/terminate-all-others. Любой вошедший.
/// </summary>
public sealed class SessionsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder security = app.MapGroup("/security/sessions").RequireAuthorization();
        security.MapGet(string.Empty, ListAsync);
        security.MapPost("/{sessionId}/terminate", TerminateAsync);
        security.MapPost("/terminate-all-others", TerminateOthersAsync);
    }

    private static async Task<EndpointResult<SessionsResponse>> ListAsync(
        [FromServices] CurrentUser user,
        [FromServices] SessionsHandler handler,
        CancellationToken cancellationToken) =>
        user.Id is { } id
            ? await handler.ListAsync(id, user.SessionId, cancellationToken)
            : Result.Failure<SessionsResponse, Error>(AuthErrors.SessionMissing());

    private static async Task<EndpointResult<SessionsResponse>> TerminateAsync(
        string sessionId,
        [FromServices] CurrentUser user,
        [FromServices] SessionsHandler handler,
        CancellationToken cancellationToken) =>
        user.Id is { } id
            ? await handler.TerminateAsync(id, sessionId, user.SessionId, cancellationToken)
            : Result.Failure<SessionsResponse, Error>(AuthErrors.SessionMissing());

    private static async Task<EndpointResult<TerminatedResponse>> TerminateOthersAsync(
        [FromServices] CurrentUser user,
        [FromServices] SessionsHandler handler,
        CancellationToken cancellationToken) =>
        user.Id is { } id
            ? await handler.TerminateOthersAsync(id, user.SessionId, cancellationToken)
            : Result.Failure<TerminatedResponse, Error>(AuthErrors.SessionMissing());
}
