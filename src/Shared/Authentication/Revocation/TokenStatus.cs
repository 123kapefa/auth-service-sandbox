namespace PlatformAuth.Revocation;

/// <summary>Итог проверки токена по отметкам.</summary>
public enum TokenStatus
{
    /// <summary>Действует.</summary>
    Valid,

    /// <summary>Отозван: войти заново.</summary>
    Revoked,

    /// <summary>Доступ изменился: обновить токен (POST /auth/refresh) — сессия жива.</summary>
    AccessChanged,

    /// <summary>
    /// Отметки прочитать не удалось (хранилище недоступно), а режим при сбое — «закрыто» (TokenRevocation:OnStoreFailure=Closed):
    /// токен не принимается, ответ 503. В режиме «открыто» (по умолчанию) вместо этого — Valid.
    /// </summary>
    Unavailable,
}
