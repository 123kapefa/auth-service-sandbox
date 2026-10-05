namespace AuthService.Contracts.Auth;

/// <summary>POST /auth/mode — в какой режим перейти (customer, manager, admin).</summary>
/// <param name="ActiveMode">Режим — любая роль пользователя в приложении.</param>
/// <param name="App">
/// Приложение проекта (orders): режим в нём. Не задано — режим в приложении сервиса входа. Страница приложения ходит
/// со своим токеном не в сервис входа, поэтому режим в нём переключают здесь — из личного кабинета.
/// </param>
public sealed record SwitchModeRequest(string? ActiveMode, string? App = null);

/// <summary>GET /auth/modes — режим в приложении проекта: какие есть и какой выбран.</summary>
/// <param name="App">Приложение (client_id): orders; сервис входа — тоже приложение.</param>
/// <param name="Name">Название приложения.</param>
/// <param name="Modes">Режимы пользователя в нём (его роли).</param>
/// <param name="ActiveMode">Выбранный; не выбирали — роль по умолчанию (так считает и токен приложения).</param>
public sealed record AppModeResponse(string App, string? Name, IReadOnlyList<string> Modes, string? ActiveMode);
