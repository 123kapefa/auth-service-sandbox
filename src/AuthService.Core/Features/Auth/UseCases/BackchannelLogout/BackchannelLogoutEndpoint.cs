using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace AuthService.Core.Features.Auth.UseCases.BackchannelLogout;

/// <summary>
/// POST /auth/backchannel-logout (form: logout_token) — вызывает Keycloak, адрес — в настройках клиента sandbox-bff
/// (backchannel.logout.url). Без входа: подлинность — подпись logout_token.
/// </summary>
public sealed class BackchannelLogoutEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/backchannel-logout", Handle).DisableAntiforgery();

    private static async Task<EndpointResult> Handle(
        [FromForm(Name = "logout_token")] string? logoutToken,
        [FromServices] BackchannelLogoutHandler handler,
        CancellationToken cancellationToken)
    {
        UnitResult<Error> result = await handler.Handle(logoutToken, cancellationToken);
        return result;
    }
}
