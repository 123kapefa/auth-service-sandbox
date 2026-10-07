namespace AuthService.Contracts.Me;

/// <summary>Профиль в личном кабинете: данные учётной записи на сервере входа.</summary>
/// <param name="Id">Id пользователя.</param>
/// <param name="Phone">Телефон с «+» — логин; человек меняет его сам (кабинет → «Сменить номер», 10.5).</param>
/// <param name="Email">Почта, если есть.</param>
/// <param name="EmailVerified">Почта подтверждена.</param>
/// <param name="CreatedAt">Когда создана учётная запись.</param>
/// <param name="PhoneChangedAt">Когда человек сам сменил номер; null — не менял.</param>
public sealed record ProfileResponse(Guid Id, string? Phone, string? Email, bool EmailVerified, DateTimeOffset CreatedAt, DateTimeOffset? PhoneChangedAt = null);
