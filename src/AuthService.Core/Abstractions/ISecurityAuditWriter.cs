namespace AuthService.Core.Abstractions;

public interface ISecurityAuditWriter
{
    Task WriteAsync(
        string eventType,
        Guid? subjectId = null,
        string? clientId = null,
        string? ipAddress = null,
        string? metadata = null,
        CancellationToken cancellationToken = default);
}
