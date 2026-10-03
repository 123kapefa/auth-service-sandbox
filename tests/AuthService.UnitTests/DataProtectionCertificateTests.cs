using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AuthService.Infrastructure.Postgres;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthService.UnitTests;

// Ключи DataProtection в базе (ими зашифрованы токены управления ключами сервисов): в бою — только под сертификатом.
public sealed class DataProtectionCertificateTests : IDisposable
{
    private readonly string _certificatePath = Path.Combine(Path.GetTempPath(), $"dp-{Guid.NewGuid():N}.pfx");
    private readonly string _otherCertificatePath = Path.Combine(Path.GetTempPath(), $"dp-{Guid.NewGuid():N}.pfx");
    private readonly DirectoryInfo _keys = Directory.CreateTempSubdirectory("dp-keys-");

    [Fact]
    public void Required_NoCertificate_ServiceDoesNotStart()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Build(new() { ["DataProtection:RequireCertificate"] = "true" }));

        Assert.Contains("DataProtection:CertificatePath", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Certificate_KeysEncrypted_NewProcessDecryptsWithSameCertificateOnly()
    {
        WriteCertificate(_certificatePath);
        WriteCertificate(_otherCertificatePath);

        // Хранилище ключей — каталог вместо базы: тот же путь, что после перезапуска сервиса (ключи — из хранилища).
        string protectedToken;
        using (ServiceProvider first = BuildWithKeysOnDisk(_certificatePath))
        {
            protectedToken = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("management-token");
        }

        string key = File.ReadAllText(Assert.Single(_keys.GetFiles("*.xml")).FullName);
        Assert.Contains("EncryptedData", key, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", key, StringComparison.Ordinal);

        using ServiceProvider restarted = BuildWithKeysOnDisk(_certificatePath);
        Assert.Equal("management-token", restarted.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedToken));

        using ServiceProvider otherCertificate = BuildWithKeysOnDisk(_otherCertificatePath);
        Assert.ThrowsAny<CryptographicException>(() =>
            otherCertificate.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedToken));
    }

    [Fact]
    public void NotRequired_NoCertificate_KeysNotEncrypted()
    {
        ServiceProvider provider = Build([]);

        Assert.Null(provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor);
    }

    public void Dispose()
    {
        File.Delete(_certificatePath);
        File.Delete(_otherCertificatePath);
        _keys.Delete(recursive: true);
    }

    private static void WriteCertificate(string path)
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new("CN=auth-service-data-protection", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, "pfx-password"));
    }

    private ServiceProvider BuildWithKeysOnDisk(string certificatePath)
    {
        IConfiguration configuration = Configuration(new()
        {
            ["DataProtection:RequireCertificate"] = "true",
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:CertificatePassword"] = "pfx-password",
        });
        IServiceCollection services = new ServiceCollection().AddLogging().AddInfrastructurePostgres(configuration);
        services.AddDataProtection().PersistKeysToFileSystem(_keys);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:AuthDb"] = "Host=localhost;Database=auth_service;Username=auth_service;Password=test";
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings) =>
        new ServiceCollection().AddLogging().AddInfrastructurePostgres(Configuration(settings)).BuildServiceProvider();
}
