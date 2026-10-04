namespace AuthService.Contracts.Me;

/// <summary>Профиль в личном кабинете: данные учётной записи на сервере входа.</summary>
/// <param name="Id">Id пользователя.</param>
/// <param name="Phone">Телефон с «+» — логин; меняется только через поддержку.</param>
/// <param name="Email">Почта, если есть.</param>
/// <param name="EmailVerified">Почта подтверждена.</param>
/// <param name="CreatedAt">Когда создана учётная запись.</param>
public sealed record ProfileResponse(Guid Id, string? Phone, string? Email, bool EmailVerified, DateTimeOffset CreatedAt);
