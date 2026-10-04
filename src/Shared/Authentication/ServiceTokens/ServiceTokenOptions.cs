namespace PlatformAuth.ServiceTokens;

/// <summary>
/// Секция ServiceToken: ключ доступа сервиса проекта (client_id + секрет), выданный в админке сервиса входа
/// («Сервисы и ключи»). По нему сервис берёт машинный токен и ходит к другим сервисам проекта.
/// </summary>
public sealed class ServiceTokenOptions
{
    public const string SECTION_NAME = "ServiceToken";

    /// <summary>Адрес realm сервера входа (как Authentication:Authority): токен — {Authority}/protocol/openid-connect/token.</summary>
    public string Authority { get; init; } = string.Empty;

    /// <summary>Адрес выдачи токенов, если сервис видит сервер входа не по Authority (контейнер → keycloak:8180).</summary>
    public string? TokenEndpoint { get; init; }

    /// <summary>client_id ключа: service-orders.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Секрет ключа. Только из переменной окружения / хранилища секретов.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>За сколько до истечения брать новый токен.</summary>
    public TimeSpan RefreshMargin { get; init; } = TimeSpan.FromSeconds(30);

    public Uri ResolveTokenEndpoint() =>
        new(string.IsNullOrWhiteSpace(TokenEndpoint)
            ? $"{Authority.TrimEnd('/')}/protocol/openid-connect/token"
            : TokenEndpoint);
}
