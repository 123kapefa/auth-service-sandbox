namespace AuthService.Contracts.Admin;

/// <summary>Страница пользователей. Общего числа нет: Keycloak считает его отдельным запросом — вместо него hasMore.</summary>
public sealed record UsersPageResponse(IReadOnlyList<UserListItemResponse> Items, int Page, int PageSize, bool HasMore);

/// <summary>Строка списка. Поля — подмножество UserListItemDto боевого сервиса.</summary>
/// <param name="Id">Id пользователя.</param>
/// <param name="Username">Логин: номер цифрами или почта.</param>
/// <param name="Phone">Телефон с «+», если есть.</param>
/// <param name="Email">Почта, если есть.</param>
/// <param name="Role">Роли сотрудника через запятую или customer.</param>
/// <param name="CreatedAt">Когда создан.</param>
/// <param name="IsRestricted">Заблокирован.</param>
public sealed record UserListItemResponse(
    Guid Id,
    string Username,
    string? Phone,
    string? Email,
    string Role,
    DateTimeOffset CreatedAt,
    bool IsRestricted);
