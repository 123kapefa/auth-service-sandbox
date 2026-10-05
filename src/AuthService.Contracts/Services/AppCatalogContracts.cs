namespace AuthService.Contracts.Services;

/// <summary>
/// PUT /auth/services/catalog — каталог приложения из его кода: права, которые код проверяет, и стартовые роли.
/// Присылает сервис приложения своим ключом (право service.catalog.declare) при каждом старте; повтор безопасен.
/// </summary>
/// <param name="Permissions">Права, которые проверяет код приложения (RequirePermissions).</param>
/// <param name="Roles">Встроенные роли — стартовый набор прав; дальше их составом управляет админ.</param>
public sealed record DeclareCatalogRequest(IReadOnlyList<DeclaredPermission>? Permissions, IReadOnlyList<DeclaredRole>? Roles);

/// <param name="Name">Имя права: части через точку, строчная латиница (orders.view).</param>
/// <param name="Description">Что даёт право — так его видит админ.</param>
public sealed record DeclaredPermission(string? Name, string? Description);

/// <param name="Name">Имя роли (режима): строчная латиница, цифры, дефис (customer, manager).</param>
/// <param name="Description">Кто это — так роль видит админ.</param>
/// <param name="Permissions">Права роли при создании: объявленные в этом же каталоге или уже заведённые в приложении.</param>
public sealed record DeclaredRole(string? Name, string? Description, IReadOnlyList<string>? Permissions);

/// <summary>Что сделал сервис входа с каталогом: создал, обновил, оставил как есть.</summary>
/// <param name="App">Приложение (client_id), в которое записан каталог.</param>
/// <param name="CreatedPermissions">Права, которых не было, — созданы.</param>
/// <param name="UpdatedPermissions">Права, у которых сменилось описание или которые были заведены в админке, — теперь из кода.</param>
/// <param name="CreatedRoles">Роли, которых не было (или пустые из add-app.py), — созданы с объявленным составом.</param>
/// <param name="KeptRoles">Роли, уже заведённые кодом или админом, — не тронуты: их составом управляет админ.</param>
/// <param name="Orphans">Права «из кода», которых больше нет в каталоге, — оставлены (могут быть в ролях).</param>
public sealed record DeclareCatalogResponse(
    string App,
    IReadOnlyList<string> CreatedPermissions,
    IReadOnlyList<string> UpdatedPermissions,
    IReadOnlyList<string> CreatedRoles,
    IReadOnlyList<string> KeptRoles,
    IReadOnlyList<string> Orphans);
