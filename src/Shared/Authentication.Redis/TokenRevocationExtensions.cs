using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PlatformAuth.Revocation;
using StackExchange.Redis;

namespace PlatformAuth.Redis;

public static class TokenRevocationExtensions
{
    /// <summary>
    /// Мгновенный отзыв токенов: проверка на каждом запросе (вместе с AddPlatformJwtAuthentication) и запись отметок.
    /// Redis — ConnectionStrings:Redis (обязателен), настройки — секция TokenRevocation. Проверка готовности — тег ready.
    /// </summary>
    public static IServiceCollection AddPlatformTokenRevocation(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString(TokenRevocationOptions.CONNECTION_STRING_NAME)
            ?? throw new InvalidOperationException($"ConnectionStrings:{TokenRevocationOptions.CONNECTION_STRING_NAME} is required.");

        services.Configure<TokenRevocationOptions>(configuration.GetSection(TokenRevocationOptions.SECTION_NAME));
        // abortConnect=false: сервис стартует и без Redis, переподключение — само; проверка отзыва в это время пропускает токены.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(ConfigurationOptions.Parse($"{connectionString},abortConnect=false")));
        services.AddSingleton<RedisTokenRevocationStore>();
        services.AddSingleton<ITokenRevocationStore, CacheInvalidatingRevocationStore>();
        services.AddSingleton<ITokenRevocationChecker, TokenRevocationChecker>();
        services.AddHybridCache();
        services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
        return services;
    }
}

/// <summary>Redis отвечает на PING.</summary>
internal sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Проверка готовности не должна висеть на таймауте Redis (5 с по умолчанию).
            await redis.GetDatabase().PingAsync().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Redis is unavailable.", exception);
        }
    }
}
