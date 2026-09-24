using AuthService.Core.Abstractions;
using AuthService.Domain.Audit;

namespace AuthService.Infrastructure.Postgres.Audit;

public sealed class PostgresSecurityAuditWriter(AuthDbContext dbContext) : ISecurityAuditWriter
{
    public async Task WriteAsync(
        string eventType,
        Guid? subjectId = null,
        string? clientId = null,
        string? ipAddress = null,
        string? metadata = null,
        CancellationToken cancellationToken = default)
    {
        dbContext.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            EventType = eventType,
            SubjectId = subjectId,
            ClientId = clientId,
            IpAddress = ipAddress,
            Metadata = metadata
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
