namespace AuthService.Domain.Sessions;

/// <summary>
/// С какого устройства начата сессия сервера входа: браузер (User-Agent) на возврате со страницы входа. Keycloak в
/// Admin API этого не отдаёт — без записи кабинет показал бы только IP. Ключ — Id сессии (sid): по нему находятся и
/// живые сессии, и входы в истории (событие LOGIN Keycloak несёт тот же Id).
/// </summary>
public sealed class LoginDevice
{
    public const int MAX_SESSION_ID_LENGTH = 100;
    public const int MAX_USER_AGENT_LENGTH = 512;

    /// <summary>Сколько хранить: как события входа в realm (eventsExpiration) — дольше история не показывается.</summary>
    public static TimeSpan Retention { get; } = TimeSpan.FromDays(30);

    private LoginDevice()
    {
    }

    public string SessionId { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }

    /// <summary>User-Agent браузера (обрезан до MAX_USER_AGENT_LENGTH); null — браузер его не прислал.</summary>
    public string? UserAgent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static LoginDevice Record(string sessionId, Guid userId, string? userAgent, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        string? agent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim();
        return new LoginDevice
        {
            SessionId = sessionId,
            UserId = userId,
            UserAgent = agent is { Length: > MAX_USER_AGENT_LENGTH } ? agent[..MAX_USER_AGENT_LENGTH] : agent,
            CreatedAt = createdAt,
        };
    }
}
