using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace PlatformAuth;

public static class AuthorizationExtensions
{
    /// <summary>Политики «роль + активный режим» (AuthPolicies). Без токена → 401, не тот режим → 403.</summary>
    public static IServiceCollection AddPlatformAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, ActiveModeRequirementHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.ADMIN_ONLY, policy => policy.RequireActiveMode(AuthModes.ADMIN))
            .AddPolicy(AuthPolicies.MANAGER_OR_ADMIN, policy => policy.RequireActiveMode(AuthModes.ADMIN, AuthModes.MANAGER))
            .AddPolicy(AuthPolicies.USER, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !context.User.HasClaim(claim => string.Equals(claim.Type, AuthClaims.CLIENT_ID, StringComparison.Ordinal))))
            .AddPolicy(AuthPolicies.SERVICE, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AuthClaims.CLIENT_ID));

        return services;
    }

    /// <summary>Только человек: токен сервиса (по ключу доступа) → 403, без токена → 401.</summary>
    public static TBuilder RequireUser<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(AuthPolicies.USER);

    /// <summary>Только сервис проекта по ключу и со всеми перечисленными правами: человек → 403, без токена → 401.</summary>
    public static TBuilder RequireService<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return builder.RequireAuthorization([AuthPolicies.SERVICE, .. permissions.Select(PermissionExtensions.ToPolicyName)]);
    }

    private static AuthorizationPolicyBuilder RequireActiveMode(this AuthorizationPolicyBuilder policy, params string[] modes) =>
        policy
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveModeRequirement(modes));
}
