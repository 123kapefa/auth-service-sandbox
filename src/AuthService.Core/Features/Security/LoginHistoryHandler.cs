using AuthService.Contracts.Security;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Features.Security;

/// <summary>
/// Свои входы — события сервера входа (LOGIN и LOGIN_ERROR; хранятся 30 дней, eventsExpiration realm), новые сверху.
/// Устройство — по сессии, которую начал вход (у ошибки входа сессии нет).
/// </summary>
public sealed class LoginHistoryHandler(IIdentityAdmin identityAdmin, LoginDevices devices)
{
    /// <summary>Сколько последних попыток показывать.</summary>
    public const int MAX_ITEMS = 50;

    private static readonly IReadOnlyCollection<string> _types = [LoginEventTypes.LOGIN, LoginEventTypes.LOGIN_ERROR];

    public async Task<Result<LoginHistoryResponse, Error>> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<LoginEvent>, Error> events =
            await identityAdmin.GetLoginEventsAsync(userId, _types, 0, MAX_ITEMS, cancellationToken);
        if (events.IsFailure)
        {
            return events.Error;
        }

        // Только свои: фильтр user Admin API уже отбирает их, проверка — на случай события без пользователя.
        List<LoginEvent> own = [.. events.Value.Where(found => found.UserId == userId)];
        IReadOnlyDictionary<string, string?> userAgents = await devices.GetUserAgentsAsync(
            userId,
            [.. own.Select(found => found.SessionId).OfType<string>().Distinct(StringComparer.Ordinal)],
            cancellationToken);

        return new LoginHistoryResponse(
        [
            .. own.Select(found => new LoginHistoryItem(
                found.Time,
                string.Equals(found.Type, LoginEventTypes.LOGIN, StringComparison.Ordinal),
                found.Error,
                found.IpAddress,
                found.SessionId is { } session ? DeviceLabels.FromUserAgent(userAgents.GetValueOrDefault(session)) : null,
                found.SessionId)),
        ]);
    }
}
