using AuthService.Core.Authorization;
using PlatformAuth;

namespace AuthService.Api.Configuration;

/// <summary>Проверка токенов Keycloak, политики «роль + режим» и права сервиса.</summary>
public static class AuthenticationConfiguration
{
    public static IServiceCollection AddAuthServiceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPlatformJwtAuthentication(configuration);
        services.AddPlatformAuthorization();
        services.AddPlatformPermissions<AuthServiceRolePermissions>();

        return services;
    }

    public static IApplicationBuilder UseAuthServiceAuthentication(this IApplicationBuilder app) =>
        app
            .UseAuthentication()
            .UseAuthorization();
}
