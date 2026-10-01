namespace PlatformAuth;

/// <summary>
/// Право, которое объявляет код сервиса: имя (его проверяет RequirePermissions) и описание для админки.
/// Сервис входа заводит такие права в Keycloak как роли клиента приложения (source=code).
/// </summary>
public sealed record PermissionDefinition(string Name, string Description);
