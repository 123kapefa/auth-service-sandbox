using System.Text.Json;

namespace AuthService.Contracts.Admin;

/// <summary>GET /auth/admin/audit — фильтры журнала (все необязательны) и страница.</summary>
public sealed record AuditRequest(
    Guid? ObjectId,
    Guid? ActorId,
    string? Action,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int? Page,
    int? PageSize);

/// <summary>Страница журнала, новые сверху.</summary>
public sealed record AuditPageResponse(IReadOnlyList<AuditEntryResponse> Items, int Page, int PageSize, bool HasMore);

/// <summary>Запись журнала — поля записи сервиса аудита боевой платформы.</summary>
/// <param name="Id">Id записи.</param>
/// <param name="OccurredAt">Когда.</param>
/// <param name="Action">Что сделано (UserRestrictionChanged…).</param>
/// <param name="ObjectType">Над чем (user).</param>
/// <param name="ObjectId">Id объекта.</param>
/// <param name="ObjectName">Имя объекта в момент действия.</param>
/// <param name="Changes">Изменения: { "поле": { "old", "new" } } и подробности.</param>
/// <param name="ActorId">Кто.</param>
/// <param name="ActorName">Телефон сотрудника.</param>
/// <param name="ActorRole">Режим, в котором он действовал.</param>
/// <param name="CorrelationId">Id запроса — найти в логах.</param>
public sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    string ObjectType,
    Guid ObjectId,
    string? ObjectName,
    JsonElement Changes,
    Guid? ActorId,
    string? ActorName,
    string? ActorRole,
    string? CorrelationId);

/// <summary>GET /auth/admin/login-events — события входа Keycloak; userId — одного пользователя.</summary>
public sealed record LoginEventsRequest(Guid? UserId, int? Page, int? PageSize);

public sealed record LoginEventsResponse(IReadOnlyList<LoginEventResponse> Items, int Page, int PageSize, bool HasMore);

/// <summary>Вход, ошибка входа или выход.</summary>
public sealed record LoginEventResponse(
    DateTimeOffset Time,
    string Type,
    Guid? UserId,
    string? Username,
    string? IpAddress,
    string? ClientId,
    string? Error);
