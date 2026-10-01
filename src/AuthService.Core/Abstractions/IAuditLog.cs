using AuthService.Domain.AuditLog;

namespace AuthService.Core.Abstractions;

/// <summary>Журнал действий сотрудников (таблица auth.audit_entries).</summary>
public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);

    /// <summary>Записи по фильтру, новые сверху; на одну больше pageSize не берётся — вместо счётчика hasMore.</summary>
    Task<AuditPage> SearchAsync(AuditQuery query, CancellationToken cancellationToken);
}

/// <summary>Фильтр журнала: все поля необязательны; page — с 1.</summary>
public sealed record AuditQuery(
    Guid? ObjectId,
    Guid? ActorId,
    string? Action,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize);

public sealed record AuditPage(IReadOnlyList<AuditEntry> Items, bool HasMore);
