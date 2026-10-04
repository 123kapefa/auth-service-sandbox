using AuthService.Core.Abstractions;
using AuthService.Domain.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using PlatformAuth;

namespace AuthService.Core.Features.Security;

/// <summary>
/// Запись устройства на возврате со страницы входа: sid и sub — из только что полученного access-токена (пришёл от
/// сервера входа по прямому каналу, подпись проверять не нужно), браузер — User-Agent запроса. «По возможности»:
/// не записалось — вход всё равно состоялся, сбой уходит в лог (в кабинете будет «Неизвестное устройство»).
/// </summary>
public sealed partial class LoginDeviceRecorder(ILoginDeviceStore store, TimeProvider timeProvider, ILogger<LoginDeviceRecorder> logger)
{
    public async Task RecordAsync(string accessToken, string? userAgent, CancellationToken cancellationToken)
    {
        if (Read(accessToken) is not { } session)
        {
            return;
        }

        try
        {
            await store.RecordAsync(LoginDevice.Record(session.Id, session.UserId, userAgent, timeProvider.GetUtcNow()), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotRecorded(logger, exception);
        }
    }

    private static (string Id, Guid UserId)? Read(string accessToken)
    {
        try
        {
            JsonWebToken jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
            return jwt.TryGetPayloadValue(AuthClaims.SESSION_ID, out string? sid)
                && !string.IsNullOrWhiteSpace(sid)
                && sid.Length <= LoginDevice.MAX_SESSION_ID_LENGTH
                && Guid.TryParse(jwt.Subject, out Guid userId)
                    ? (sid, userId)
                    : null;
        }
        catch (ArgumentException)
        {
            return null; // не JWT — сессию не узнать
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login device was not recorded")]
    private static partial void LogNotRecorded(ILogger logger, Exception exception);
}
