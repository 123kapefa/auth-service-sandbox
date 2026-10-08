using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace AuthService.Infrastructure.Postgres.DataProtection;

/// <summary>
/// Сертификаты, которыми шифруются ключи DataProtection в базе. Текущий шифрует новые ключи; прежний (на время смены
/// сертификата) только расшифровывает старые, пока <see cref="DataProtectionKeyRewrapService"/> не перешифрует их текущим.
/// Сертификат — файлом PFX (<c>CertificatePath</c>) или содержимым PFX в base64 (<c>Certificate</c>, из хранилища секретов:
/// на машине нет файла); пароль — <c>CertificatePassword</c>. Для прежнего — то же с приставкой Previous.
/// </summary>
internal sealed record DataProtectionCertificates(X509Certificate2? Current, X509Certificate2? Previous)
{
    public const string SECTION_NAME = "DataProtection";

    public static DataProtectionCertificates Load(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(SECTION_NAME);
        X509Certificate2? current = LoadOne(section, string.Empty);
        X509Certificate2? previous = LoadOne(section, "Previous");
        if (previous is not null && current is null)
        {
            throw new InvalidOperationException(
                "DataProtection:PreviousCertificate is set without DataProtection:Certificate: the previous certificate only decrypts old keys, the current one is required.");
        }

        return new DataProtectionCertificates(current, previous);
    }

    private static X509Certificate2? LoadOne(IConfigurationSection section, string prefix)
    {
        string? path = section[$"{prefix}CertificatePath"];
        string? content = section[$"{prefix}Certificate"];
        string? password = section[$"{prefix}CertificatePassword"];
        if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                $"DataProtection:{prefix}CertificatePath and DataProtection:{prefix}Certificate are both set: use one of them.");
        }

        if (!string.IsNullOrWhiteSpace(content))
        {
            byte[] pfx;
            try
            {
                pfx = Convert.FromBase64String(content.Trim());
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException($"DataProtection:{prefix}Certificate must be a PFX in base64.", exception);
            }

            return X509CertificateLoader.LoadPkcs12(pfx, password);
        }

        return string.IsNullOrWhiteSpace(path) ? null : X509CertificateLoader.LoadPkcs12FromFile(path, password);
    }
}
