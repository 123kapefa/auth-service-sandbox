using AuthService.Contracts.Admin;
using AuthService.Core.Features.Auth;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin.UseCases.AdminMe;

/// <summary>
/// GET /auth/admin/me — с чего начинается админка: права проекта и приложения под управлением. Любой вошедший:
/// у покупателя прав и приложений нет — админка покажет «нет доступа».
/// </summary>
public sealed class AdminMeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/auth/admin/me", Handle).RequireUser();

    private static async Task<EndpointResult<AdminMeResponse>> Handle(
        [FromServices] CurrentUser caller,
        [FromServices] ManagedApps managedApps,
        CancellationToken cancellationToken)
    {
        if (caller.Id is not { } id)
        {
            return Result.Failure<AdminMeResponse, Error>(AuthErrors.SessionMissing());
        }

        Result<IReadOnlyList<ManagedAppResponse>, Error> apps = await managedApps.ListAsync(caller, cancellationToken);
        return apps.IsFailure
            ? Result.Failure<AdminMeResponse, Error>(apps.Error)
            : Result.Success<AdminMeResponse, Error>(new AdminMeResponse(
                id, caller.Phone, caller.ActiveMode, [.. caller.Permissions.Order(StringComparer.Ordinal)], apps.Value));
    }
}
