namespace PlatformAuth;

/// <summary>
/// Карта «роль → права». Это знание конкретного сервиса: каждый сервис задаёт свою карту,
/// а библиотека только применяет её. Права в токен не кладутся — смена карты не требует перевыпуска токенов.
/// </summary>
public interface IRolePermissionMap
{
    IReadOnlyCollection<string> GetPermissions(string role);
}
