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

    public Task RecordAsync(string action, IdentityUser target, object changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return RecordAsync(action, AuditActions.USER_OBJECT, target.Id, target.Username, changes, cancellationToken);
    }

    /// <summary>Действие над объектом любого типа: роль, право…</summary>
    public async Task RecordAsync(
        string action,
        string objectType,
        Guid objectId,
        string? objectName,
        object changes,
        CancellationToken cancellationToken)
    {
        AuditEntry entry = AuditEntry.Create(
            timeProvider.GetUtcNow(),
            action,
            objectType,
            objectId,
            objectName,
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
            LogNotRecorded(logger, action, objectId, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit entry {Action} for object {ObjectId} was not recorded")]
    private static partial void LogNotRecorded(ILogger logger, string action, Guid objectId, Exception exception);
}
