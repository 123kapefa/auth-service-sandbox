namespace PlatformAuth.Redis;

/// <summary>Секция TokenRevocation. Адрес Redis — ConnectionStrings:Redis.</summary>
public sealed class TokenRevocationOptions
{
    public const string SECTION_NAME = "TokenRevocation";
    public const string CONNECTION_STRING_NAME = "Redis";

    /// <summary>
    /// Сколько хранить отметку: не меньше срока жизни access-токена (в Keycloak — 15 минут) — потом старые токены
    /// истекают сами. С запасом на расхождение часов.
    /// </summary>
    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromMinutes(16);

    /// <summary>
    /// Кэш ответа в памяти процесса: серия запросов в одну секунду не ходит в Redis каждый раз. Это и есть задержка
    /// отзыва на других экземплярах сервиса (свой экземпляр, поставивший отметку, сбрасывает кэш сразу) — держать
    /// короткой: стандарт платформы — 2–3 секунды. 0 — без кэша.
    /// </summary>
    public TimeSpan LocalCache { get; init; } = TimeSpan.FromSeconds(2);
}
