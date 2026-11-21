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

    /// <summary>
    /// Что делать, когда отметки прочитать нельзя (Redis недоступен). Open (по умолчанию): токен пропускается — он и так
    /// живёт не дольше 15 минут, вход не падает вместе с Redis, но на это время отзыв не работает (заблокированный человек
    /// ещё входит). Closed: токен не принимается (503), безопаснее, но недоступность Redis кладёт и вход, и API.
    /// </summary>
    public StoreFailureMode OnStoreFailure { get; init; } = StoreFailureMode.Open;
}

/// <summary>Режим при недоступном хранилище отметок.</summary>
public enum StoreFailureMode
{
    /// <summary>Токен пропускается, в лог — предупреждение.</summary>
    Open,

    /// <summary>Токен не принимается: 503 и Retry-After.</summary>
    Closed,
}
