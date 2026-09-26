using Microsoft.AspNetCore.Authorization;
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
            .AddPolicy(AuthPolicies.MANAGER_OR_ADMIN, policy => policy.RequireActiveMode(AuthModes.ADMIN, AuthModes.MANAGER));

        return services;
    }

    private static AuthorizationPolicyBuilder RequireActiveMode(this AuthorizationPolicyBuilder policy, params string[] modes) =>
        policy
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveModeRequirement(modes));
}
