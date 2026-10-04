namespace AuthService.Contracts.Admin;

/// <summary>
/// PUT /auth/admin/users/{id}/role — роль: customer, manager или admin. В отличие от боевого сервиса пароля в запросе нет:
/// для выдачи admin нужен недавний вход (GET /auth/login?reauth=true) — Keycloak спросит пароль и код 2FA, если она включена.
/// </summary>
public sealed record SetRoleRequest(string? Role);
