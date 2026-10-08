using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AuthService.Infrastructure.Postgres;
using AuthService.Infrastructure.Postgres.DataProtection;
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

    [Fact]
    public void CertificateInBase64_KeysEncrypted_SameAsFile()
    {
        WriteCertificate(_certificatePath);
        string protectedToken;
        using (ServiceProvider fromFile = BuildWithCertificates(new() { ["DataProtection:CertificatePath"] = _certificatePath }))
        {
            protectedToken = fromFile.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("management-token");
        }

        using ServiceProvider fromSecret = BuildWithCertificates(new()
        {
            ["DataProtection:Certificate"] = Convert.ToBase64String(File.ReadAllBytes(_certificatePath)),
        });

        Assert.Equal("management-token", fromSecret.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedToken));
    }

    [Fact]
    public void BothPathAndBase64_ServiceDoesNotStart()
    {
        WriteCertificate(_certificatePath);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Build(new()
        {
            ["DataProtection:CertificatePath"] = _certificatePath,
            ["DataProtection:Certificate"] = Convert.ToBase64String(File.ReadAllBytes(_certificatePath)),
            ["DataProtection:CertificatePassword"] = "pfx-password",
        }));

        Assert.Contains("both set", error.Message, StringComparison.Ordinal);
    }

    // Смена сертификата: старые ключи (ими зашифрованы cookie входа и токены управления) читаются с прежним сертификатом,
    // после перешифровки — с одним новым; прежний больше не нужен, ключи и зашифрованное ими не меняются.
    [Fact]
    public void Rotation_PreviousCertificate_OldTokensReadable_AfterRewrapNewCertificateAlone()
    {
        WriteCertificate(_certificatePath);
        WriteCertificate(_otherCertificatePath);
        string protectedToken;
        using (ServiceProvider old = BuildWithKeysOnDisk(_certificatePath))
        {
            protectedToken = old.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("session");
        }

        using (ServiceProvider overlap = BuildWithCertificates(new()
               {
                   ["DataProtection:CertificatePath"] = _otherCertificatePath,
                   ["DataProtection:PreviousCertificatePath"] = _certificatePath,
                   ["DataProtection:PreviousCertificatePassword"] = "pfx-password",
               }))
        {
            Assert.Equal("session", overlap.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedToken));
        }

        RewrapKeysOnDisk(current: _otherCertificatePath, previous: _certificatePath);

        using ServiceProvider rotated = BuildWithKeysOnDisk(_otherCertificatePath);
        Assert.Equal("session", rotated.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedToken));

        using ServiceProvider oldOnly = BuildWithKeysOnDisk(_certificatePath);
        Assert.ThrowsAny<CryptographicException>(() =>
            oldOnly.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedToken));
    }

    [Fact]
    public void Rewrap_AlreadyUnderCurrentCertificate_NoChange()
    {
        WriteCertificate(_certificatePath);
        using (ServiceProvider provider = BuildWithKeysOnDisk(_certificatePath))
        {
            provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("session");
        }

        using X509Certificate2 current = LoadCertificate(_certificatePath);
        string xml = File.ReadAllText(Assert.Single(_keys.GetFiles("*.xml")).FullName);

        Assert.Null(DataProtectionKeyRewrap.Rewrap(xml, current, [current]));
    }

    // Ключ создан без сертификата (стенд до хранилища секретов) — перешифровка закрывает его сертификатом.
    [Fact]
    public void Rewrap_UnencryptedKey_EncryptedWithCurrentCertificate()
    {
        WriteCertificate(_certificatePath);
        string protectedToken;
        IServiceCollection plainServices = new ServiceCollection().AddLogging();
        plainServices.AddDataProtection().SetApplicationName("auth-service").PersistKeysToFileSystem(_keys);
        using (ServiceProvider plain = plainServices.BuildServiceProvider())
        {
            protectedToken = plain.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("session");
        }

        FileInfo file = Assert.Single(_keys.GetFiles("*.xml"));
        Assert.Contains("<masterKey", File.ReadAllText(file.FullName), StringComparison.Ordinal);

        RewrapKeysOnDisk(current: _certificatePath, previous: null);

        string key = File.ReadAllText(file.FullName);
        Assert.Contains("EncryptedData", key, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", key, StringComparison.Ordinal);
        using ServiceProvider encrypted = BuildWithKeysOnDisk(_certificatePath);
        Assert.Equal("session", encrypted.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedToken));
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

    private static X509Certificate2 LoadCertificate(string path) => X509CertificateLoader.LoadPkcs12FromFile(path, "pfx-password");

    private void RewrapKeysOnDisk(string current, string? previous)
    {
        using X509Certificate2 currentCertificate = LoadCertificate(current);
        using X509Certificate2? previousCertificate = previous is null ? null : LoadCertificate(previous);
        X509Certificate2[] decryptors = previousCertificate is null ? [currentCertificate] : [currentCertificate, previousCertificate];
        foreach (FileInfo file in _keys.GetFiles("*.xml"))
        {
            string xml = DataProtectionKeyRewrap.Rewrap(File.ReadAllText(file.FullName), currentCertificate, decryptors)
                ?? throw new InvalidOperationException("Key was expected to change.");
            File.WriteAllText(file.FullName, xml);
        }
    }

    private ServiceProvider BuildWithKeysOnDisk(string certificatePath) =>
        BuildWithCertificates(new() { ["DataProtection:CertificatePath"] = certificatePath });

    private ServiceProvider BuildWithCertificates(Dictionary<string, string?> certificates)
    {
        Dictionary<string, string?> settings = new(certificates)
        {
            ["DataProtection:RequireCertificate"] = "true",
            ["DataProtection:CertificatePassword"] = "pfx-password",
        };
        IConfiguration configuration = Configuration(settings);
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
