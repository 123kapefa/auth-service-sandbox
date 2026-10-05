namespace AuthService.Contracts.Admin;

/// <summary>Сервис проекта с ключом доступа (секрет не показывается — только при выдаче и перевыпуске).</summary>
/// <param name="Code">Код: orders.</param>
/// <param name="ClientId">client_id в Keycloak: service-orders.</param>
/// <param name="Name">Название: «Заказы».</param>
/// <param name="IssuedAt">Когда выдан ключ.</param>
/// <param name="RotatedAt">Когда перевыпущен последний раз.</param>
/// <param name="Permissions">
/// Что сервису можно: в сервисе входа — service.users.read, service.access.check, service.catalog.declare; в приложениях
/// проекта — «приложение:право» (notifications:notifications.send). null — не удалось прочитать.
/// </param>
public sealed record ServiceKeyResponse(
    string Code,
    string ClientId,
    string Name,
    DateTimeOffset IssuedAt,
    DateTimeOffset? RotatedAt,
    IReadOnlyList<string>? Permissions);

/// <summary>Ключ выдан или перевыпущен: секрет показывается один раз — сохраните его в настройки сервиса.</summary>
/// <param name="Service">Сервис.</param>
/// <param name="ClientSecret">Секрет: вместе с client_id — ключ, по которому сервис получает токены.</param>
public sealed record IssuedServiceKeyResponse(ServiceKeyResponse Service, string ClientSecret);

/// <summary>POST /auth/admin/services — выдать ключ новому сервису.</summary>
/// <param name="Code">Код: строчная латиница, цифры, дефис (orders, notifications).</param>
/// <param name="Name">Название для админки.</param>
/// <param name="Permissions">Права сервиса: service.* и «приложение:право» (можно пусто — выдать позже).</param>
public sealed record IssueServiceKeyRequest(string? Code, string? Name, IReadOnlyList<string>? Permissions = null);

/// <summary>POST /auth/admin/services/{code}/rotate — перевыпустить ключ (тело можно не передавать).</summary>
/// <param name="KeepTokens">
/// Плановая смена: выданные по старому секрету токены доживают свой срок, сервис успевает взять новый секрет без
/// остановки. По умолчанию (ключ мог утечь) — они перестают действовать сразу.
/// </param>
public sealed record RotateServiceKeyRequest(bool? KeepTokens);

/// <summary>PUT /auth/admin/services/{code}/permissions — права сервиса целиком: service.* и «приложение:право».</summary>
public sealed record SetServicePermissionsRequest(IReadOnlyList<string>? Permissions);

/// <summary>GET /auth/admin/services/grantable — права приложения проекта, которые можно выдать ключу.</summary>
/// <param name="App">Код приложения (client_id): notifications.</param>
/// <param name="Name">Название приложения.</param>
/// <param name="Permissions">Его права.</param>
public sealed record GrantableAppPermissionsResponse(string App, string? Name, IReadOnlyList<GrantablePermissionResponse> Permissions);

/// <summary>Право приложения для ключа.</summary>
/// <param name="Key">Как его выдать ключу: notifications:notifications.send.</param>
/// <param name="Name">Имя права в приложении: notifications.send.</param>
/// <param name="Description">Описание.</param>
public sealed record GrantablePermissionResponse(string Key, string Name, string? Description);
