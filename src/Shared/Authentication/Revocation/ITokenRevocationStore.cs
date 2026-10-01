namespace PlatformAuth.Revocation;

/// <summary>Где хранятся отметки (Redis — PlatformAuth.Redis). Читают все сервисы, пишет сервис входа.</summary>
public interface ITokenRevocationStore
{
    /// <summary>Отметки для токена: пользователь, его сессия, приложение и роль активного режима.</summary>
    Task<RevocationMarks> GetAsync(TokenKey key, CancellationToken cancellationToken);

    /// <summary>Все токены пользователя, выданные до этого момента, недействительны.</summary>
    Task RevokeUserAsync(string subject, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Все токены сессии недействительны.</summary>
    Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Токены приложения для пользователя, выданные до этого момента, недействительны.</summary>
    Task RevokeAppAsync(string authorizedParty, string subject, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Роли пользователя изменились: токены с версией доступа меньше этой надо обновить.</summary>
    Task MarkAccessChangedAsync(string subject, long version, CancellationToken cancellationToken);

    /// <summary>Состав роли изменился (или её удалили): токены в этом режиме с версией роли меньше этой надо обновить.</summary>
    Task MarkModeChangedAsync(string rolesClient, string role, long version, CancellationToken cancellationToken);
}
