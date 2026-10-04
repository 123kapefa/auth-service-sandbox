namespace AuthService.Contracts.Admin;

/// <summary>Роли пользователя в приложении. Роль = режим: режимы — все его роли.</summary>
/// <param name="UserId">Id пользователя.</param>
/// <param name="Roles">Выданные роли — их меняет PUT.</param>
/// <param name="DefaultRoles">Роли по умолчанию (customer у всех) — снять нельзя.</param>
/// <param name="Modes">Режимы: роли по умолчанию и выданные.</param>
/// <param name="ActiveMode">Активный режим в приложении (null — не выбран).</param>
/// <param name="HasAccess">Открыт ли доступ к приложению; null — доступ к нему не закрывается.</param>
/// <param name="AssignableRoles">Роли приложения, которые можно выдать (без ролей по умолчанию), с описаниями.</param>
public sealed record UserRolesResponse(
    Guid UserId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> DefaultRoles,
    IReadOnlyList<string> Modes,
    string? ActiveMode,
    bool? HasAccess,
    IReadOnlyList<AssignableRoleResponse> AssignableRoles);

/// <summary>Роль, которую можно выдать: имя и описание для админки.</summary>
public sealed record AssignableRoleResponse(string Name, string? Description);

/// <summary>
/// PUT /auth/admin/apps/{clientId}/users/{userId}/roles — выданные роли, ровно этот список (пустой — только роли по
/// умолчанию). Роли по умолчанию в списке можно указать — они ничего не меняют.
/// </summary>
public sealed record SetUserRolesRequest(IReadOnlyList<string>? Roles);
