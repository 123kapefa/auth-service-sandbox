namespace AuthService.Contracts.Security;

/// <summary>Сессии пользователя — как у боевого сервиса ({ items: [...] }).</summary>
public sealed record SessionsResponse(IReadOnlyList<SessionResponse> Items);

/// <summary>Одна сессия (вход на устройстве). Поля — как у боевого сервиса, плюс приложения сессии.</summary>
/// <param name="Id">Id сессии.</param>
/// <param name="UserAgent">Браузер: Keycloak в Admin API его не отдаёт — пока null.</param>
/// <param name="IpAddress">IP, с которого вошли.</param>
/// <param name="DeviceLabel">Устройство: так же — пока null.</param>
/// <param name="CreatedAt">Когда вошли.</param>
/// <param name="LastSeenAt">Последняя активность (вход или обновление токена).</param>
/// <param name="IsCurrent">Это та сессия, из которой сделан запрос.</param>
/// <param name="Apps">Приложения, в которые вошли в этой сессии.</param>
public sealed record SessionResponse(
    string Id,
    string? UserAgent,
    string? IpAddress,
    string? DeviceLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    bool IsCurrent,
    IReadOnlyList<string> Apps);

/// <summary>Сколько сессий завершено.</summary>
public sealed record TerminatedResponse(int Terminated);
