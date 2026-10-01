namespace PlatformAuth.Revocation;

/// <summary>
/// Отметки об отзыве для одного токена: по пользователю, по сессии и по приложению. Время — секунды Unix:
/// токен, выданный раньше отметки, недействителен; выданный позже (после повторного входа) — действует.
/// </summary>
/// <param name="UserRevokedAt">
/// Блокировка, смена роли, «выйти везде»: токены пользователя, выданные строго раньше (iat в секундах — токены той же
/// секунды закрывают отметки по сессиям, которые сервис входа ставит вместе с этой).
/// </param>
/// <param name="SessionRevoked">Сессия завершена: все её токены.</param>
/// <param name="AppRevokedAt">
/// Закрыт доступ к приложению: токены этого приложения, выданные в эту секунду и раньше (новый вход в закрытое
/// приложение Keycloak и так не пускает).
/// </param>
public sealed record RevocationMarks(long? UserRevokedAt, bool SessionRevoked, long? AppRevokedAt)
{
    public static readonly RevocationMarks None = new(null, false, null);

    /// <summary>Отозван ли токен, выданный в issuedAt (секунды Unix).</summary>
    public bool Revokes(long issuedAt) =>
        SessionRevoked
        || (UserRevokedAt is { } user && issuedAt < user)
        || (AppRevokedAt is { } app && issuedAt <= app);
}
