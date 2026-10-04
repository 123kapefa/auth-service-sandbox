using AuthService.Core.Abstractions;
using AuthService.Infrastructure.Postgres.AuditLog;
using AuthService.Infrastructure.Postgres.ServiceKeys;
using AuthService.Infrastructure.Postgres.Sessions;
using System.Security.Cryptography.X509Certificates;
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
        services.AddScoped<ILoginDeviceStore, EfLoginDeviceStore>();
        IDataProtectionBuilder dataProtection = services.AddDataProtection()
            .SetApplicationName("auth-service")
            .PersistKeysToDbContext<AuthDbContext>();

        // В бою ключи DataProtection шифруются сертификатом: иначе дамп базы содержит и ключи, и то, что ими зашифровано
        // (токены управления ключами сервисов). DataProtection:CertificatePath (PFX) и :CertificatePassword — из секретов.
        // В бою сертификат обязателен (флаг RequireCertificate в настройках Production): без него сервис не стартует.
        string? certificate = configuration["DataProtection:CertificatePath"];
        if (string.IsNullOrWhiteSpace(certificate) && configuration.GetValue<bool>("DataProtection:RequireCertificate"))
        {
            throw new InvalidOperationException(
                "DataProtection:CertificatePath is required: keys protecting service key management tokens must be encrypted with a certificate (PFX).");
        }

        if (!string.IsNullOrWhiteSpace(certificate))
        {
            dataProtection.ProtectKeysWithCertificate(
                X509CertificateLoader.LoadPkcs12FromFile(certificate, configuration["DataProtection:CertificatePassword"]));
        }

        return services;
    }
}
