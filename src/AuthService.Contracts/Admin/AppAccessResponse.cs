namespace AuthService.Contracts.Admin;

/// <summary>Приложение проекта и доступ к нему пользователя.</summary>
/// <param name="ClientId">Клиент приложения в Keycloak.</param>
/// <param name="Name">Название приложения.</param>
/// <param name="HasAccess">false — доступ к приложению закрыт; аккаунт при этом рабочий.</param>
public sealed record AppAccessResponse(string ClientId, string? Name, bool HasAccess);
