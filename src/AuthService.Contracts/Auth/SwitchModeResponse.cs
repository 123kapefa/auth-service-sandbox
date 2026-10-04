namespace AuthService.Contracts.Auth;

/// <summary>Режим переключён. Поля — как у боевого сервиса (SwitchModeResponse).</summary>
/// <param name="UserId">Id пользователя.</param>
/// <param name="Phone">Телефон с «+», если есть.</param>
/// <param name="Role">Роли сотрудника через запятую (manager, admin) или customer.</param>
/// <param name="AvailableRoles">Режимы, в которые можно перейти; customer — всегда.</param>
/// <param name="ActiveMode">Новый активный режим.</param>
/// <param name="HasPassword">Задан ли пароль (мог входить только по коду).</param>
public sealed record SwitchModeResponse(
    Guid UserId,
    string? Phone,
    string Role,
    IReadOnlyList<string> AvailableRoles,
    string ActiveMode,
    bool HasPassword);
