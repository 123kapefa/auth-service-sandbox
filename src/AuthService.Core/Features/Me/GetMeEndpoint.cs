using AuthService.Contracts.Me;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Me;

/// <summary>GET /me/ — любой вошедший пользователь видит, что о нём знает сервис.</summary>
public sealed class GetMeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/me/", Handle).RequireUser();

    private static EndpointResult<MeResponse> Handle([FromServices] CurrentUser user) =>
        Result.Success<MeResponse, Error>(new MeResponse(
            user.Id,
            user.Phone,
            user.ActiveMode,
            user.Roles,
            user.AvailableModes,
            [.. user.Permissions.Order(StringComparer.Ordinal)]));
}
