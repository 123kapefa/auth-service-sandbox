namespace PlatformAuth.Revocation;

/// <summary>
/// Отметки для одного токена: отзыв (по пользователю, сессии, приложению) и «доступ изменился» (версии доступа
/// пользователя и роли активного режима). Время — секунды Unix: токен, выданный раньше отметки, недействителен;
/// выданный позже (после повторного входа) — действует.
/// </summary>
/// <param name="UserRevokedAt">
/// Блокировка, «выйти везде»: токены пользователя, выданные строго раньше (iat в секундах — токены той же
/// секунды закрывают отметки по сессиям, которые сервис входа ставит вместе с этой).
/// </param>
/// <param name="SessionRevoked">Сессия завершена: все её токены.</param>
/// <param name="AppRevokedAt">
/// Закрыт доступ к приложению: токены этого приложения, выданные в эту секунду и раньше (новый вход в закрытое
/// приложение Keycloak и так не пускает).
/// </param>
/// <param name="AccessVersion">Текущая версия доступа пользователя (роли выданы или сняты).</param>
/// <param name="ModeVersion">Текущая версия роли активного режима (изменён её состав или она удалена).</param>
public sealed record RevocationMarks(
    long? UserRevokedAt,
    bool SessionRevoked,
    long? AppRevokedAt,
    long? AccessVersion = null,
    long? ModeVersion = null)
{
    public static readonly RevocationMarks None = new(null, false, null);

    /// <summary>Отозван ли токен, выданный в issuedAt (секунды Unix).</summary>
    public bool Revokes(long issuedAt) =>
        SessionRevoked
        || (UserRevokedAt is { } user && issuedAt < user)
        || (AppRevokedAt is { } app && issuedAt <= app);

    /// <summary>
    /// Изменился ли доступ: версии в токене старше текущих. Токен без версии — версия 0. Сессия жива: клиенту
    /// достаточно обновить токен (POST /auth/refresh), выходить не нужно.
    /// </summary>
    public bool AccessChanged(long? tokenAccessVersion, long? tokenModeVersion) =>
        (AccessVersion is { } access && (tokenAccessVersion ?? 0) < access)
        || (ModeVersion is { } mode && (tokenModeVersion ?? 0) < mode);
}
