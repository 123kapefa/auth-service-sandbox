using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin;

/// <summary>
/// Право на приложение {clientId}: у админа проекта — право в токене (активный режим, все приложения), у админа
/// приложения — то же право среди его ролей в самом приложении (например, roles.manage в роли admin «Менеджера 1С»).
/// Админ приложения управляет только своим приложением: в приложение сервиса (auth-api) так не войти — там
/// действуют только права режима из токена.
/// </summary>
public static class AppPermissionFilter
{
    public const string CLIENT_ID_ROUTE = "clientId";

    public static TBuilder RequireAppPermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireUser();
        builder.AddEndpointFilter(async (context, next) =>
            await AllowedAsync(context.HttpContext, permission)
                ? await next(context)
                : new EndpointResult(UnitResult.Failure(AdminErrors.AppForbidden())));
        return builder;
    }

    private static async Task<bool> AllowedAsync(HttpContext httpContext, string permission)
    {
        CurrentUser caller = httpContext.RequestServices.GetRequiredService<CurrentUser>();
        if (caller.HasPermission(permission))
        {
            return true;
        }

        string audience = httpContext.RequestServices.GetRequiredService<IOptions<JwtAuthenticationOptions>>().Value.Audience;
        if (caller.Id is not { } id
            || httpContext.GetRouteValue(CLIENT_ID_ROUTE) is not string clientId
            || string.Equals(clientId, audience, StringComparison.Ordinal))
        {
            return false;
        }

        // Действующие роли в приложении — с правами в их составе.
        Result<IReadOnlyList<string>, Error> roles = await httpContext.RequestServices.GetRequiredService<IIdentityAdmin>()
            .GetEffectiveClientRolesAsync(id, clientId, httpContext.RequestAborted);
        return roles.IsSuccess && roles.Value.Contains(permission, StringComparer.Ordinal);
    }
}
