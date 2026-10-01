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
}
