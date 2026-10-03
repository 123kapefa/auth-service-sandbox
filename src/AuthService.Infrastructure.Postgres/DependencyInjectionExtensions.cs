using AuthService.Core.Abstractions;
using AuthService.Infrastructure.Postgres.AuditLog;
using AuthService.Infrastructure.Postgres.ServiceKeys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.Infrastructure.Postgres;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructurePostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AuthDb")
            ?? throw new InvalidOperationException("ConnectionStrings:AuthDb is required.");

        services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseNpgsql(connectionString, AuthDbContext.ConfigureNpgsql);
            options.UseSnakeCaseNamingConvention();
        });
        services.AddScoped<IAuditLog, EfAuditLog>();
        services.AddScoped<IServiceKeyStore, EfServiceKeyStore>();
        services.AddDataProtection()
            .SetApplicationName("auth-service")
            .PersistKeysToDbContext<AuthDbContext>();

        return services;
    }
}
