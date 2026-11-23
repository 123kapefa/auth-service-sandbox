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
        // Cookie с access-токеном читается под тем же именем, под каким сервис её ставит (по https — с приставкой __Host-).
        configuration["Authentication:AccessTokenCookie"] = AuthCookies.AccessToken(configuration.GetValue("Cookies:Secure", true));
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
            // Лимиты частоты: после проверки токена (ключ сервиса — client_id из него), до авторизации и эндпоинтов.
            .UseAuthServiceRateLimiting()
            .UseAuthorization();
}
