namespace AuthService.Domain.Audit;

public sealed class SecurityAuditEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public required string EventType { get; init; }

    public Guid? SubjectId { get; init; }

    public string? ClientId { get; init; }

    public string? IpAddress { get; init; }

    public string? Metadata { get; init; }
}
