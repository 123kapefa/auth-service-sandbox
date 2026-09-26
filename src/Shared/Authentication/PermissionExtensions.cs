using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace PlatformAuth;

public static class PermissionExtensions
{
    /// <summary>Права по активному режиму с картой сервиса TMap.</summary>
    public static IServiceCollection AddPlatformPermissions<TMap>(this IServiceCollection services)
        where TMap : class, IRolePermissionMap
    {
        services.AddSingleton<IRolePermissionMap, TMap>();
        services.AddSingleton<IPermissionResolver, ActiveModePermissionResolver>();
        services.AddSingleton<IAuthorizationHandler, PermissionRequirementHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

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
