using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Infrastructure.Postgres.AuditLog;

/// <summary>Журнал в таблице auth.audit_entries.</summary>
internal sealed class EfAuditLog(AuthDbContext db) : IAuditLog
{
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuditPage> SearchAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<AuditEntry> entries = db.AuditEntries.AsNoTracking();
        if (query.ObjectId is { } objectId)
        {
            entries = entries.Where(entry => entry.ObjectId == objectId);
        }

        if (query.ActorId is { } actorId)
        {
            entries = entries.Where(entry => entry.ActorId == actorId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            entries = entries.Where(entry => entry.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.App))
        {
            entries = entries.Where(entry => entry.App == query.App);
        }

        if (query.From is { } from)
        {
            entries = entries.Where(entry => entry.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(entry => entry.OccurredAt <= to);
        }

        List<AuditEntry> page = await entries
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize + 1)
            .ToListAsync(cancellationToken);

        return new AuditPage([.. page.Take(query.PageSize)], page.Count > query.PageSize);
    }
}
