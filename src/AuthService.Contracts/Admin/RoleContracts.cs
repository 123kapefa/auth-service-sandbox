namespace AuthService.Contracts.Admin;

/// <summary>Роль приложения (она же режим) и её права.</summary>
/// <param name="Name">Имя: manager, support…</param>
/// <param name="Description">Описание для админки.</param>
/// <param name="Builtin">Встроенная (customer, manager, admin) — удалить нельзя.</param>
/// <param name="Permissions">Права роли.</param>
public sealed record RoleResponse(string Name, string? Description, bool Builtin, IReadOnlyList<string> Permissions);

/// <summary>POST /auth/admin/apps/{clientId}/roles — новая роль: имя (строчная латиница, цифры, дефис), описание, права.</summary>
public sealed record CreateRoleRequest(string? Name, string? Description, IReadOnlyList<string>? Permissions);

/// <summary>PUT /auth/admin/apps/{clientId}/roles/{name} — описание и состав прав (ровно этот список).</summary>
public sealed record UpdateRoleRequest(string? Description, IReadOnlyList<string>? Permissions);
