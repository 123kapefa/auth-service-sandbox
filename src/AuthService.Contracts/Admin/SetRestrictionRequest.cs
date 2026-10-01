namespace AuthService.Contracts.Admin;

/// <summary>PUT /auth/admin/users/{id}/restriction — заблокировать (true) или разблокировать (false); причина — до 500 символов.</summary>
public sealed record SetRestrictionRequest(bool? Restricted, string? Reason);
