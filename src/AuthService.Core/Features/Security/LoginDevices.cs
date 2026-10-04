using AuthService.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace AuthService.Core.Features.Security;

/// <summary>
/// Чтение устройств сессий «по возможности»: база недоступна — список сессий и история входов всё равно
/// показываются, только без устройства (сбой уходит в лог).
/// </summary>
public sealed partial class LoginDevices(ILoginDeviceStore store, ILogger<LoginDevices> logger)
{
    public async Task<IReadOnlyDictionary<string, string?>> GetUserAgentsAsync(
        Guid userId,
        IReadOnlyCollection<string> sessionIds,
        CancellationToken cancellationToken)
    {
        if (sessionIds.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        try
        {
            return await store.GetUserAgentsAsync(userId, sessionIds, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotRead(logger, exception);
            return new Dictionary<string, string?>();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login devices were not read")]
    private static partial void LogNotRead(ILogger logger, Exception exception);
}
