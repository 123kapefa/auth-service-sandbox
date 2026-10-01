namespace AuthService.Contracts.Admin;

/// <summary>Карточка пользователя для админки.</summary>
/// <param name="Id">Id пользователя.</param>
/// <param name="Username">Логин: номер цифрами или почта.</param>
/// <param name="Phone">Телефон с «+», если есть.</param>
/// <param name="Email">Почта, если есть.</param>
/// <param name="Role">Роли сотрудника через запятую или customer.</param>
/// <param name="AvailableRoles">Режимы, доступные пользователю; customer — всегда.</param>
/// <param name="ActiveMode">Активный режим.</param>
/// <param name="CreatedAt">Когда создан.</param>
/// <param name="IsRestricted">Заблокирован.</param>
/// <param name="RestrictionReason">Причина блокировки, если указана.</param>
/// <param name="RestrictedAt">Когда заблокирован.</param>
/// <param name="HasPassword">Задан ли пароль.</param>
/// <param name="LastLoginAt">Последний вход (события Keycloak хранятся 30 дней; раньше — null).</param>
public sealed record UserDetailResponse(
    Guid Id,
    string Username,
    string? Phone,
    string? Email,
    string Role,
    IReadOnlyList<string> AvailableRoles,
    string? ActiveMode,
    DateTimeOffset CreatedAt,
    bool IsRestricted,
    string? RestrictionReason,
    DateTimeOffset? RestrictedAt,
    bool HasPassword,
    DateTimeOffset? LastLoginAt);
