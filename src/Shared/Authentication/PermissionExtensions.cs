using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace PlatformAuth;

public static class PermissionExtensions
{
    /// <summary>Права из токена (claim permissions — по активному режиму) и CurrentUser («кто вызвал») для эндпоинтов.</summary>
    public static IServiceCollection AddPlatformPermissions(this IServiceCollection services)
    {
        services.AddOptions();
        services.AddSingleton<IPermissionResolver, TokenPermissionResolver>();
        services.AddSingleton<IAuthorizationHandler, PermissionRequirementHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUser>();

        return services;
    }

    /// <summary>Эндпоинт требует все перечисленные права. Без токена → 401, без права → 403.</summary>
    public static TBuilder RequirePermissions<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return builder.RequireAuthorization([.. permissions.Select(ToPolicyName)]);
    }

    public static string ToPolicyName(string permission) => PermissionPolicyProvider.POLICY_PREFIX + permission;
}
