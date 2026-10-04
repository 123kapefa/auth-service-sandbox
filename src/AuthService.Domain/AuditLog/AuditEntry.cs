namespace AuthService.Domain.AuditLog;

/// <summary>
/// Запись журнала действий сотрудников: кто, что и с кем сделал. Только добавляется — не меняется и не удаляется.
/// Поля — как у записи сервиса аудита боевой платформы (Action, ObjectType, ObjectId, Changes, Actor*).
/// </summary>
public sealed class AuditEntry
{
    public const int MAX_ACTION_LENGTH = 100;
    public const int MAX_NAME_LENGTH = 255;

    private AuditEntry()
    {
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Что сделано: AuditActions (UserRestrictionChanged, UserRoleChanged…).</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>Над чем: тип объекта (user).</summary>
    public string ObjectType { get; private set; } = string.Empty;

    public Guid ObjectId { get; private set; }

    /// <summary>Как объект звался в момент действия (логин пользователя).</summary>
    public string? ObjectName { get; private set; }

    /// <summary>Изменения — JSON: { "поле": { "old": …, "new": … } } и подробности (причина, приложение).</summary>
    public string Changes { get; private set; } = "{}";

    public Guid? ActorId { get; private set; }

    public string? ActorName { get; private set; }

    /// <summary>Режим, в котором действовал сотрудник (admin, manager).</summary>
    public string? ActorRole { get; private set; }

    /// <summary>Id запроса — связать запись с логами.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Приложение (clientId), которого касается действие; null — уровень аккаунта (блокировка, «выйти везде»).</summary>
    public string? App { get; private set; }

    public static AuditEntry Create(
        DateTimeOffset occurredAt,
        string action,
        string objectType,
        Guid objectId,
        string? objectName,
        string changes,
        Guid? actorId,
        string? actorName,
        string? actorRole,
        string? correlationId,
        string? app = null) =>
        new()
        {
            Id = Guid.CreateVersion7(occurredAt),
            OccurredAt = occurredAt,
            Action = action,
            ObjectType = objectType,
            ObjectId = objectId,
            ObjectName = objectName,
            Changes = changes,
            ActorId = actorId,
            ActorName = actorName,
            ActorRole = actorRole,
            CorrelationId = correlationId,
            App = app,
        };
}
