using AuthService.Core.Abstractions;
using AuthService.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Infrastructure.Postgres.Sessions;

/// <summary>Устройства сессий в таблице auth.login_devices.</summary>
internal sealed class EfLoginDeviceStore(AuthDbContext db) : ILoginDeviceStore
{
    public async Task RecordAsync(LoginDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Вход в другое приложение в той же сессии — та же запись: устройство то же, время — первого входа.
        if (!await db.LoginDevices.AnyAsync(found => found.SessionId == device.SessionId, cancellationToken))
        {
            db.LoginDevices.Add(device);
            await db.SaveChangesAsync(cancellationToken);
        }

        DateTimeOffset expired = device.CreatedAt - LoginDevice.Retention;
        await db.LoginDevices.Where(found => found.CreatedAt < expired).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetUserAgentsAsync(
        Guid userId,
        IReadOnlyCollection<string> sessionIds,
        CancellationToken cancellationToken) =>
        await db.LoginDevices
            .AsNoTracking()
            .Where(found => found.UserId == userId && sessionIds.Contains(found.SessionId))
            .ToDictionaryAsync(found => found.SessionId, found => found.UserAgent, StringComparer.Ordinal, cancellationToken);
}
