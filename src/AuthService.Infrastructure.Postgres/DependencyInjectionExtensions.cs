using AuthService.Core.Abstractions;
using AuthService.Infrastructure.Postgres.AuditLog;
using AuthService.Infrastructure.Postgres.DataProtection;
using AuthService.Infrastructure.Postgres.ServiceKeys;
using AuthService.Infrastructure.Postgres.Sessions;
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
        // (токены управления ключами сервисов). Сертификат — DataProtection:CertificatePath (PFX) или :Certificate (PFX в
        // base64, из хранилища секретов), пароль — :CertificatePassword. В бою обязателен (RequireCertificate в настройках
        // Production): без него сервис не стартует. Смена сертификата — с прежним в :PreviousCertificate* (docs/secrets.md).
        DataProtectionCertificates certificates = DataProtectionCertificates.Load(configuration);
        if (certificates.Current is null && configuration.GetValue<bool>("DataProtection:RequireCertificate"))
        {
            throw new InvalidOperationException(
                "DataProtection:CertificatePath or DataProtection:Certificate is required: keys protecting service key management tokens must be encrypted with a certificate (PFX).");
        }

        if (certificates.Current is { } current)
        {
            dataProtection.ProtectKeysWithCertificate(current);
            if (certificates.Previous is { } previous)
            {
                dataProtection.UnprotectKeysWithAnyCertificate(current, previous);
            }

            services.AddSingleton(certificates);
            services.AddHostedService<DataProtectionKeyRewrapService>();
        }

        return services;
    }
}
