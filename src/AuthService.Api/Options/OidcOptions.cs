namespace AuthService.Api.Options;

public sealed class OidcOptions
{
    public const string SectionName = "Oidc";

    public string Issuer { get; init; } = "http://localhost:8080/";

    public OidcKeyOptions Keys { get; init; } = new();
}

public sealed class OidcKeyOptions
{
    public string? SigningCertificatePath { get; init; }

    public string? SigningCertificatePassword { get; init; }

    public string? PreviousSigningCertificatePath { get; init; }

    public string? PreviousSigningCertificatePassword { get; init; }

    public string? EncryptionCertificatePath { get; init; }

    public string? EncryptionCertificatePassword { get; init; }
}
