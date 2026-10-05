using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PlatformAuth.ServiceTokens;

public static class ServiceTokenExtensions
{
    /// <summary>Машинный токен сервиса по ключу доступа (секция ServiceToken).</summary>
    public static IServiceCollection AddPlatformServiceToken(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ServiceTokenOptions>()
            .Bind(configuration.GetSection(ServiceTokenOptions.SECTION_NAME))
            .PostConfigure(options =>
            {
                // Адрес сервера входа — тот же, что для проверки токенов: второй раз его не задают.
                if (string.IsNullOrWhiteSpace(options.Authority))
                {
                    IConfigurationSection authentication = configuration.GetSection(JwtAuthenticationOptions.SECTION_NAME);
                    options.Authority = authentication[nameof(JwtAuthenticationOptions.InternalAuthority)] is { Length: > 0 } inside
                        ? inside
                        : authentication[nameof(JwtAuthenticationOptions.Authority)] ?? string.Empty;
                }
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.ClientId)
                                 && (!string.IsNullOrWhiteSpace(options.ClientSecret) || !string.IsNullOrWhiteSpace(options.ClientSecretFile))
                                 && (!string.IsNullOrWhiteSpace(options.Authority) || !string.IsNullOrWhiteSpace(options.TokenEndpoint)),
                "ServiceToken: нужны ClientId, ClientSecret (или ClientSecretFile) и адрес сервера входа (Authentication:Authority).")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(ServiceTokenProvider.HTTP_CLIENT);
        services.TryAddSingleton<IServiceTokenProvider, ServiceTokenProvider>();
        services.TryAddTransient<ServiceTokenHandler>();
        return services;
    }

    /// <summary>Запросы этого HttpClient — с токеном сервиса (нужен AddPlatformServiceToken).</summary>
    public static IHttpClientBuilder AddServiceToken(this IHttpClientBuilder builder) =>
        builder.AddHttpMessageHandler<ServiceTokenHandler>();
}
