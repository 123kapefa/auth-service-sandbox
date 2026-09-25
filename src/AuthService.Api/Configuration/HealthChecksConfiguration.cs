using AuthService.Infrastructure.Postgres;

namespace AuthService.Api.Configuration;

/// <summary>Проверки здоровья: /health/live — процесс жив, /health/ready — готов обслуживать запросы.</summary>
public static class HealthChecksConfiguration
{
    public static IServiceCollection AddAuthServiceHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks();

        return services;
    }

    public static IEndpointRouteBuilder MapAuthServiceHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live");
        app.MapGet("/health/ready", async (AuthDbContext dbContext, CancellationToken cancellationToken) =>
            await dbContext.Database.CanConnectAsync(cancellationToken)
                ? Results.Ok(new { status = "ready" })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

        return app;
    }
}
