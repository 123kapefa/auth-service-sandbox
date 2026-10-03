namespace AuthService.Contracts.Admin;

/// <summary>Сервис проекта с ключом доступа (секрет не показывается — только при выдаче и перевыпуске).</summary>
/// <param name="Code">Код: orders.</param>
/// <param name="ClientId">client_id в Keycloak: service-orders.</param>
/// <param name="Name">Название: «Заказы».</param>
/// <param name="IssuedAt">Когда выдан ключ.</param>
/// <param name="RotatedAt">Когда перевыпущен последний раз.</param>
/// <param name="Permissions">Что сервису можно в сервисе входа: service.users.read, service.access.check.</param>
public sealed record ServiceKeyResponse(
    string Code,
    string ClientId,
    string Name,
    DateTimeOffset IssuedAt,
    DateTimeOffset? RotatedAt,
    IReadOnlyList<string> Permissions);

/// <summary>Ключ выдан или перевыпущен: секрет показывается один раз — сохраните его в настройки сервиса.</summary>
/// <param name="Service">Сервис.</param>
/// <param name="ClientSecret">Секрет: вместе с client_id — ключ, по которому сервис получает токены.</param>
public sealed record IssuedServiceKeyResponse(ServiceKeyResponse Service, string ClientSecret);

/// <summary>POST /auth/admin/services — выдать ключ новому сервису.</summary>
/// <param name="Code">Код: строчная латиница, цифры, дефис (orders, notifications).</param>
/// <param name="Name">Название для админки.</param>
/// <param name="Permissions">Права сервиса в сервисе входа (можно пусто — только токены для других сервисов проекта).</param>
public sealed record IssueServiceKeyRequest(string? Code, string? Name, IReadOnlyList<string>? Permissions = null);

/// <summary>PUT /auth/admin/services/{code}/permissions — права сервиса в сервисе входа целиком.</summary>
public sealed record SetServicePermissionsRequest(IReadOnlyList<string>? Permissions);
