using AuthService.Infrastructure.Postgres;

namespace AuthService.Api.Configuration;

/// <summary>Всё, что сервис регистрирует в DI. Каждая область — своим вызовом из своего файла.</summary>
public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddAuthServiceRegistrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddInfrastructurePostgres(configuration);
        services.AddAuthServiceHealthChecks();

        return services;
    }
}
