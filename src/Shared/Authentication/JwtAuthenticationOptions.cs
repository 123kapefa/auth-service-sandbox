namespace PlatformAuth;

/// <summary>Секция Authentication: откуда брать ключи и что ожидать в токене.</summary>
public sealed class JwtAuthenticationOptions
{
    public const string SECTION_NAME = "Authentication";

    /// <summary>Адрес realm сервера входа, например http://localhost:8180/realms/sandbox.</summary>
    public string Authority { get; init; } = string.Empty;

    /// <summary>
    /// Адрес discovery, если сервис видит сервер входа не по Authority (контейнер → keycloak:8180).
    /// Не задан — {Authority}/.well-known/openid-configuration.
    /// </summary>
    public string? MetadataAddress { get; init; }

    /// <summary>Ожидаемый iss. Не задан — равен Authority.</summary>
    public string? ValidIssuer { get; init; }

    /// <summary>Ожидаемый aud — кому адресован токен.</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>Требовать https для discovery и ключей. false — только локально.</summary>
    public bool RequireHttpsMetadata { get; init; } = true;

    public string ResolveIssuer() => string.IsNullOrWhiteSpace(ValidIssuer) ? Authority : ValidIssuer;
}
