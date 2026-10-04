namespace AuthService.Contracts.Auth;

/// <summary>Токены обновлены (сами токены — в cookie, в тело не попадают).</summary>
/// <param name="AccessTokenExpiresAt">Когда истечёт новый access-токен.</param>
/// <param name="Changes">Что изменилось в доступе по сравнению с прошлым токеном; null — ничего (или прошлого токена нет).</param>
public sealed record RefreshResponse(DateTimeOffset AccessTokenExpiresAt, AccessChangesResponse? Changes = null);

/// <summary>Разница доступа после умного рефреша — для плашки «стало доступно / больше недоступно».</summary>
/// <param name="AddedPermissions">Права, которых не было.</param>
/// <param name="RemovedPermissions">Права, которых больше нет.</param>
/// <param name="AddedRoles">Новые роли (режимы).</param>
/// <param name="RemovedRoles">Снятые роли (режимы).</param>
/// <param name="ActiveMode">Активный режим в новом токене.</param>
public sealed record AccessChangesResponse(
    IReadOnlyList<string> AddedPermissions,
    IReadOnlyList<string> RemovedPermissions,
    IReadOnlyList<string> AddedRoles,
    IReadOnlyList<string> RemovedRoles,
    string? ActiveMode);
