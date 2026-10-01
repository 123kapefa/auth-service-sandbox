namespace AuthService.Contracts.Admin;

/// <summary>Пользователи для админа приложения: найти человека и выдать ему роль. Без данных аккаунта (блокировка и т. п.).</summary>
public sealed record AppUsersPageResponse(IReadOnlyList<AppUserResponse> Items, int Page, int PageSize, bool HasMore);

/// <summary>Пользователь и его выданные роли в приложении.</summary>
/// <param name="Id">Id пользователя.</param>
/// <param name="Username">Логин: номер цифрами или почта.</param>
/// <param name="Phone">Телефон с «+», если есть.</param>
/// <param name="Email">Почта, если есть.</param>
/// <param name="Roles">Выданные роли в приложении (без ролей по умолчанию).</param>
public sealed record AppUserResponse(Guid Id, string Username, string? Phone, string? Email, IReadOnlyList<string> Roles);
