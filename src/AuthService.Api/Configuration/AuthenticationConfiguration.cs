using AuthService.Core.Authorization;
using AuthService.Infrastructure.Keycloak;
using PlatformAuth;
using PlatformAuth.Redis;

namespace AuthService.Api.Configuration;

/// <summary>Проверка токенов Keycloak, политики «роль + режим», права сервиса и клиент входа (Keycloak).</summary>
public static class AuthenticationConfiguration
{
    public static IServiceCollection AddAuthServiceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPlatformJwtAuthentication(configuration);
        services.AddPlatformTokenRevocation(configuration);
        services.AddPlatformAuthorization();
        services.AddPlatformPermissions();
        services.AddInfrastructureKeycloak(configuration);

        return services;
    }

    public static IApplicationBuilder UseAuthServiceAuthentication(this IApplicationBuilder app) =>
        app
            .UseAuthentication()
            .UseAuthorization();
}
