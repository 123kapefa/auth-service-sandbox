namespace PlatformAuth;

/// <summary>Секция Authentication: откуда брать ключи и что ожидать в токене.</summary>
public sealed class JwtAuthenticationOptions
{
    public const string SECTION_NAME = "Authentication";

    /// <summary>Адрес realm сервера входа, например http://localhost:8180/realms/shop.</summary>
    public string Authority { get; init; } = string.Empty;

    /// <summary>
    /// Тот же realm, но адресом изнутри сети, если сервис видит сервер входа не по Authority (контейнер →
    /// http://keycloak:8180/realms/shop). Из него — адрес ключей (discovery) и выдачи токенов по ключу (ServiceToken);
    /// iss в токенах по-прежнему — Authority. Не задан — всё по Authority.
    /// </summary>
    public string? InternalAuthority { get; init; }

    /// <summary>
    /// Адрес discovery, если нужен особый. Не задан — {InternalAuthority или Authority}/.well-known/openid-configuration.
    /// </summary>
    public string? MetadataAddress { get; init; }

    /// <summary>Ожидаемый iss. Не задан — равен Authority.</summary>
    public string? ValidIssuer { get; init; }

    /// <summary>Ожидаемый aud — кому адресован токен.</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Cookie, из которой берётся access-токен, если нет заголовка Authorization (accessToken — cookie сервиса входа).
    /// Не задана — только заголовок: чужому сервису cookie сервиса входа не нужна (на localhost браузер отправит её на
    /// любой порт).
    /// </summary>
    public string? AccessTokenCookie { get; init; }

    /// <summary>Требовать https для discovery и ключей. false — только локально.</summary>
    public bool RequireHttpsMetadata { get; init; } = true;

    public string ResolveIssuer() => string.IsNullOrWhiteSpace(ValidIssuer) ? Authority : ValidIssuer;

    /// <summary>Discovery: MetadataAddress, иначе — по InternalAuthority; null — по Authority (так делает JwtBearer).</summary>
    public string? ResolveMetadataAddress() =>
        !string.IsNullOrWhiteSpace(MetadataAddress) ? MetadataAddress
        : !string.IsNullOrWhiteSpace(InternalAuthority) ? $"{InternalAuthority.TrimEnd('/')}/.well-known/openid-configuration"
        : null;
}
