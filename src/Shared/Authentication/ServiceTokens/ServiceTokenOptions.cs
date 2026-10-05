namespace PlatformAuth.ServiceTokens;

/// <summary>
/// Секция ServiceToken: ключ доступа сервиса проекта (client_id + секрет), выданный в админке сервиса входа
/// («Сервисы и ключи»). По нему сервис берёт машинный токен и ходит к другим сервисам проекта.
/// </summary>
public sealed class ServiceTokenOptions
{
    public const string SECTION_NAME = "ServiceToken";

    /// <summary>
    /// Адрес realm сервера входа: токен — {Authority}/protocol/openid-connect/token. Не задан — из секции Authentication
    /// (InternalAuthority, а без него — Authority): обычно задавать не нужно.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Адрес выдачи токенов, если сервис видит сервер входа не по Authority (контейнер → keycloak:8180).</summary>
    public string? TokenEndpoint { get; init; }

    /// <summary>client_id ключа: service-orders.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Секрет ключа. Только из переменной окружения / хранилища секретов.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// Файл с секретом ключа (Docker/Kubernetes secret) — вместо ClientSecret. Читается при каждом запросе нового токена:
    /// ключ перевыпустили и положили новый секрет в файл — сервис берёт его сам, без перезапуска.
    /// </summary>
    public string? ClientSecretFile { get; init; }

    /// <summary>За сколько до истечения брать новый токен.</summary>
    public TimeSpan RefreshMargin { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Секрет: из файла (если задан ClientSecretFile), иначе ClientSecret.</summary>
    public string ResolveClientSecret() =>
        string.IsNullOrWhiteSpace(ClientSecretFile) ? ClientSecret : File.ReadAllText(ClientSecretFile).Trim();

    public Uri ResolveTokenEndpoint() =>
        new(string.IsNullOrWhiteSpace(TokenEndpoint)
            ? $"{Authority.TrimEnd('/')}/protocol/openid-connect/token"
            : TokenEndpoint);
}
