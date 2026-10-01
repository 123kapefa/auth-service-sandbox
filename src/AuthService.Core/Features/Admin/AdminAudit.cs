using System.Diagnostics;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using Microsoft.Extensions.Logging;
using PlatformAuth;

namespace AuthService.Core.Features.Admin;

/// <summary>
/// Запись действия сотрудника в журнал: кто (из токена запроса), что и с кем. Как в боевом сервисе — «по возможности»:
/// журнал недоступен — действие всё равно выполнено, сбой уходит в лог.
/// </summary>
public sealed partial class AdminAudit(IAuditLog log, CurrentUser actor, TimeProvider timeProvider, ILogger<AdminAudit> logger)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(string action, IdentityUser target, object changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        AuditEntry entry = AuditEntry.Create(
            timeProvider.GetUtcNow(),
            action,
            AuditActions.USER_OBJECT,
            target.Id,
            target.Username,
            JsonSerializer.Serialize(changes, _json),
            actor.Id,
            actor.Phone,
            actor.ActiveMode,
            Activity.Current?.TraceId.ToString());
        try
        {
            await log.RecordAsync(entry, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotRecorded(logger, action, target.Id, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit entry {Action} for user {UserId} was not recorded")]
    private static partial void LogNotRecorded(ILogger logger, string action, Guid userId, Exception exception);
}
