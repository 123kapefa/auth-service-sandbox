namespace PlatformAuth.Revocation;

/// <summary>Где хранятся отметки об отзыве (Redis — PlatformAuth.Redis). Читают все сервисы, пишет сервис входа.</summary>
public interface ITokenRevocationStore
{
    /// <summary>Отметки для токена: пользователь, его сессия и приложение, которому выдан токен.</summary>
    Task<RevocationMarks> GetAsync(string subject, string? sessionId, string? authorizedParty, CancellationToken cancellationToken);

    /// <summary>Все токены пользователя, выданные до этого момента, недействительны.</summary>
    Task RevokeUserAsync(string subject, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Все токены сессии недействительны.</summary>
    Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Токены приложения для пользователя, выданные до этого момента, недействительны.</summary>
    Task RevokeAppAsync(string authorizedParty, string subject, DateTimeOffset at, CancellationToken cancellationToken);
}
