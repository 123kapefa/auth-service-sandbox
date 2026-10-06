using System.Collections.Concurrent;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Журнал в памяти вместо Postgres; Broken — журнал «упал».</summary>
public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public bool Broken { get; set; }

    public IReadOnlyList<AuditEntry> Entries => [.. _entries];

    public void Reset()
    {
        _entries.Clear();
        Broken = false;
    }

    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        if (Broken)
        {
            throw new InvalidOperationException("Журнал недоступен.");
        }

        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task<AuditPage> SearchAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        List<AuditEntry> found =
        [
            .. _entries
                .Where(entry => query.ObjectId is null || entry.ObjectId == query.ObjectId)
                .Where(entry => query.ActorId is null || entry.ActorId == query.ActorId)
                .Where(entry => query.Action is null || entry.Action == query.Action)
                .Where(entry => query.App is null || entry.App == query.App)
                .Where(entry => query.From is null || entry.OccurredAt >= query.From)
                .Where(entry => query.To is null || entry.OccurredAt <= query.To)
                .OrderByDescending(entry => entry.OccurredAt)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize + 1),
        ];
        return Task.FromResult(new AuditPage([.. found.Take(query.PageSize)], found.Count > query.PageSize));
    }
}
