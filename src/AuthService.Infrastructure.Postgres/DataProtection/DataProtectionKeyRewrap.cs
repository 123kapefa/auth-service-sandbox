using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuthService.Infrastructure.Postgres.DataProtection;

/// <summary>
/// Перешифровка ключа DataProtection текущим сертификатом. Ключ (XML) хранит секрет в элементе encryptedSecret,
/// зашифрованном сертификатом на момент создания ключа. После смены сертификата старые ключи по-прежнему нужны
/// (ими зашифрованы cookie входа и токены управления ключами сервисов), поэтому их секрет расшифровывается прежним
/// сертификатом и шифруется текущим — сами ключи и всё, что ими зашифровано, не меняются.
/// </summary>
internal static class DataProtectionKeyRewrap
{
    private static readonly XNamespace _dataProtection = "http://schemas.asp.net/2015/03/dataProtection";
    private static readonly XName _encryptedSecret = _dataProtection + "encryptedSecret";
    private static readonly XName _requiresEncryption = _dataProtection + "requiresEncryption";

    /// <summary>
    /// XML ключа, перешифрованный <paramref name="current"/>, или null, если ключ уже под ним. Незашифрованный секрет
    /// (ключ создан до появления сертификата) тоже шифруется. Секрет расшифровывается любым из <paramref name="decryptors"/>.
    /// </summary>
    public static string? Rewrap(string xml, X509Certificate2 current, IReadOnlyCollection<X509Certificate2> decryptors)
    {
        XElement key = XElement.Parse(xml);
        var changed = false;

        foreach (XElement secret in key.Descendants(_encryptedSecret).ToList())
        {
            XElement encrypted = secret.Elements().Single();
            if (IsEncryptedFor(encrypted, current))
            {
                continue;
            }

            secret.ReplaceWith(Encrypt(Decrypt(encrypted, decryptors), current));
            changed = true;
        }

        foreach (XElement plain in key.Descendants()
                     .Where(element => (bool?)element.Attribute(_requiresEncryption) == true)
                     .ToList())
        {
            plain.ReplaceWith(Encrypt(plain, current));
            changed = true;
        }

        return changed ? key.ToString(SaveOptions.DisableFormatting) : null;
    }

    private static XElement Encrypt(XElement plain, X509Certificate2 certificate)
    {
        EncryptedXmlInfo info = new CertificateXmlEncryptor(certificate, NullLoggerFactory.Instance).Encrypt(plain);
        return new XElement(
            _encryptedSecret,
            new XAttribute("decryptorType", info.DecryptorType.AssemblyQualifiedName!),
            info.EncryptedElement);
    }

    // Как EncryptedXmlDecryptor DataProtection: документ-обёртка, расшифровка на месте, ключ — по сертификату из KeyInfo.
    private static XElement Decrypt(XElement encrypted, IReadOnlyCollection<X509Certificate2> decryptors)
    {
        XmlDocument document = new() { PreserveWhitespace = true };
        using (XmlReader reader = new XElement("root", encrypted).CreateReader())
        {
            document.Load(reader);
        }

        new CertificateEncryptedXml(document, decryptors).DecryptDocument();
        XmlNode decrypted = document.DocumentElement!.FirstChild!;
        using XmlReader result = decrypted.CreateNavigator()!.ReadSubtree();
        return XElement.Load(result);
    }

    private static bool IsEncryptedFor(XElement encrypted, X509Certificate2 certificate)
    {
        XNamespace dsig = SignedXml.XmlDsigNamespaceUrl;
        return encrypted.Descendants(dsig + "X509Certificate")
            .Any(element => Convert.FromBase64String(element.Value).AsSpan().SequenceEqual(certificate.RawData));
    }

    private sealed class CertificateEncryptedXml(XmlDocument document, IReadOnlyCollection<X509Certificate2> certificates)
        : EncryptedXml(document)
    {
        public override byte[]? DecryptEncryptedKey(EncryptedKey encryptedKey)
        {
            ArgumentNullException.ThrowIfNull(encryptedKey);
            if (encryptedKey.KeyInfo is not null && encryptedKey.CipherData.CipherValue is { } cipher)
            {
                foreach (KeyInfoX509Data data in encryptedKey.KeyInfo.OfType<KeyInfoX509Data>())
                {
                    foreach (X509Certificate candidate in data.Certificates?.OfType<X509Certificate>() ?? [])
                    {
                        X509Certificate2? match = certificates.FirstOrDefault(certificate =>
                            certificate.RawData.AsSpan().SequenceEqual(candidate.GetRawCertData()));
                        using RSA? rsa = match?.GetRSAPrivateKey();
                        if (rsa is not null)
                        {
                            bool oaep = string.Equals(encryptedKey.EncryptionMethod?.KeyAlgorithm, XmlEncRSAOAEPUrl, StringComparison.Ordinal);
                            return DecryptKey(cipher, rsa, oaep);
                        }
                    }
                }
            }

            throw new CryptographicException("The key is encrypted with a certificate that is neither current nor previous.");
        }
    }
}
