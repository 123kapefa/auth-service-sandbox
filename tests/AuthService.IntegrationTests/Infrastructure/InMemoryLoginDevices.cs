using System.Collections.Concurrent;
using AuthService.Core.Abstractions;
using AuthService.Domain.Sessions;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Устройства сессий в памяти вместо Postgres; Broken — база «упала».</summary>
public sealed class InMemoryLoginDevices : ILoginDeviceStore
{
    private readonly ConcurrentDictionary<string, LoginDevice> _devices = new(StringComparer.Ordinal);

    public bool Broken { get; set; }

    public IReadOnlyCollection<LoginDevice> Devices => [.. _devices.Values];

    public void Reset()
    {
        _devices.Clear();
        Broken = false;
    }

    public void Add(TestUser user, string sessionId, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(user);
        _devices[sessionId] = LoginDevice.Record(sessionId, user.Id, userAgent, DateTimeOffset.UtcNow);
    }

    public Task RecordAsync(LoginDevice device, CancellationToken cancellationToken)
    {
        ThrowIfBroken();
        _devices.TryAdd(device.SessionId, device);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string?>> GetUserAgentsAsync(
        Guid userId,
        IReadOnlyCollection<string> sessionIds,
        CancellationToken cancellationToken)
    {
        ThrowIfBroken();
        return Task.FromResult<IReadOnlyDictionary<string, string?>>(_devices.Values
            .Where(device => device.UserId == userId && sessionIds.Contains(device.SessionId))
            .ToDictionary(device => device.SessionId, device => device.UserAgent, StringComparer.Ordinal));
    }

    private void ThrowIfBroken()
    {
        if (Broken)
        {
            throw new InvalidOperationException("База недоступна.");
        }
    }
}
