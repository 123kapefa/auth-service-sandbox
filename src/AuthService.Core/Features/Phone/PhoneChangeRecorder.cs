using System.Diagnostics;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Phone;

/// <summary>
/// Запись в журнал о смене номера самим человеком (10.5). Номер меняется на странице Keycloak (действие
/// platform-update-phone): сервис узнаёт о смене, когда человек возвращается из неё (callback с action_status=success),
/// и берёт прежний и новый номер из атрибутов пользователя. Одна смена — одна запись, сколько бы раз ни вернулись.
/// «По возможности»: журнал или Keycloak недоступны — человек всё равно вошёл, сбой — в лог. В журнале событий Keycloak
/// смена есть всегда.
/// </summary>
public sealed partial class PhoneChangeRecorder(
    IIdentityAdmin identityAdmin,
    IAuditLog log,
    TimeProvider timeProvider,
    ILogger<PhoneChangeRecorder> logger)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (Subject(accessToken) is not { } token)
        {
            return;
        }

        try
        {
            Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(token.UserId, cancellationToken);
            if (user.IsFailure || user.Value.PhoneChangedAt is not { } changedAt)
            {
                return;
            }

            AuditPage recorded = await log.SearchAsync(
                new AuditQuery(token.UserId, null, AuditActions.USER_PHONE_CHANGED, changedAt, null, 1, 1),
                cancellationToken);
            if (recorded.Items.Count > 0)
            {
                return;
            }

            await log.RecordAsync(
                AuditEntry.Create(
                    timeProvider.GetUtcNow(),
                    AuditActions.USER_PHONE_CHANGED,
                    AuditActions.USER_OBJECT,
                    token.UserId,
                    user.Value.Username,
                    JsonSerializer.Serialize(new { phone = new { old = user.Value.PreviousPhone, @new = user.Value.Phone } }, _json),
                    token.UserId,
                    user.Value.Phone,
                    token.ActiveMode,
                    Activity.Current?.TraceId.ToString()),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotRecorded(logger, token.UserId, exception);
        }
    }

    private static (Guid UserId, string? ActiveMode)? Subject(string accessToken)
    {
        try
        {
            JsonWebToken jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
            jwt.TryGetPayloadValue(AuthClaims.ACTIVE_MODE, out string? mode);
            return Guid.TryParse(jwt.Subject, out Guid userId) ? (userId, mode) : null;
        }
        catch (ArgumentException)
        {
            return null; // не JWT
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Phone change of user {UserId} was not recorded in the audit log")]
    private static partial void LogNotRecorded(ILogger logger, Guid userId, Exception exception);
}
