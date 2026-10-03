using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth.ServiceTokens;

namespace AuthService.Client;

public static class AuthServiceClientExtensions
{
    /// <summary>
    /// IAuthServiceClient: адрес сервиса входа — AuthService:BaseUrl, ключ сервиса — секция ServiceToken
    /// (подключается здесь же, если ещё не подключён).
    /// </summary>
    public static IServiceCollection AddAuthServiceClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string baseUrl = configuration["AuthService:BaseUrl"]
            ?? throw new InvalidOperationException("Не задан адрес сервиса входа AuthService:BaseUrl");

        services.AddPlatformServiceToken(configuration);
        services.AddHttpClient<IAuthServiceClient, AuthServiceClient>(client => client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"))
            .AddServiceToken();
        return services;
    }
}
