namespace AuthService.Contracts.Services;

/// <summary>Пользователь для сервиса проекта (право service.users.read).</summary>
/// <param name="Id">Id (sub токенов пользователя).</param>
/// <param name="Username">Логин: телефон цифрами или почта.</param>
/// <param name="Phone">Телефон: +79990000001.</param>
/// <param name="Email">Почта.</param>
/// <param name="Enabled">false — аккаунт заблокирован.</param>
/// <param name="RestrictionReason">Причина блокировки.</param>
/// <param name="CreatedAt">Когда создан.</param>
public sealed record ServiceUserResponse(
    Guid Id,
    string Username,
    string? Phone,
    string? Email,
    bool Enabled,
    string? RestrictionReason,
    DateTimeOffset CreatedAt);

/// <summary>Есть ли у пользователя право в приложении сейчас (право service.access.check).</summary>
/// <param name="Allowed">Да — аккаунт не заблокирован, доступ к приложению открыт, право есть в активном режиме.</param>
/// <param name="Reason">Почему нет: user.blocked, app.access_denied, app.no_mode, permission.missing; null — разрешено.</param>
/// <param name="ActiveMode">Активный режим пользователя в приложении.</param>
/// <param name="Permissions">Права активного режима.</param>
public sealed record AccessCheckResponse(bool Allowed, string? Reason, string? ActiveMode, IReadOnlyList<string> Permissions);
