namespace AuthService.Contracts.Admin;

/// <summary>PUT /auth/admin/users/{id}/apps/{clientId}/access — открыть (true) или закрыть (false) доступ к приложению.</summary>
public sealed record SetAppAccessRequest(bool? HasAccess);
