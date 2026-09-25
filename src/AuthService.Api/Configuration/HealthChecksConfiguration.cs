using AuthService.Infrastructure.Postgres;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace AuthService.Api.Configuration;

/// <summary>Проверки здоровья: /health/live — процесс жив, /health/ready — готов обслуживать запросы.</summary>
public static class HealthChecksConfiguration
{
    // Проверки с этим тегом входят в /health/ready: зависимости, без которых сервис не может работать.
    private const string READY_TAG = "ready";

    public static IServiceCollection AddAuthServiceHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<AuthDbContext>("postgresql", tags: [READY_TAG]);

        return services;
    }

    public static IEndpointRouteBuilder MapAuthServiceHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // live: ни одной проверки — отвечает, пока процесс принимает запросы.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

        // ready: только проверки с тегом ready. Unhealthy → 503.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(READY_TAG),
        });

        return app;
    }
}
