using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuthService.Infrastructure.Postgres.DataProtection;

/// <summary>
/// При старте перешифровывает ключи DataProtection в базе текущим сертификатом (<see cref="DataProtectionKeyRewrap"/>).
/// Смена сертификата: сервис запускается с новым сертификатом и прежним (PreviousCertificate*) — прежний нужен, пока
/// ключи не перешифрованы; после записи в журнале «re-encrypted» прежний убирают из настроек. Ключи, которые уже под
/// текущим сертификатом, не трогаются, поэтому повторный запуск ничего не меняет.
/// </summary>
internal sealed partial class DataProtectionKeyRewrapService(
    IServiceScopeFactory scopes,
    DataProtectionCertificates certificates,
    ILogger<DataProtectionKeyRewrapService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (certificates.Current is not { } current)
        {
            return;
        }

        X509Certificate2[] decryptors = certificates.Previous is { } previous ? [current, previous] : [current];
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            AuthDbContext db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            List<DataProtectionKey> keys = await db.DataProtectionKeys.ToListAsync(stoppingToken);
            var rewrapped = 0;
            var failed = 0;
            foreach (DataProtectionKey key in keys.Where(key => key.Xml is not null))
            {
                try
                {
                    if (DataProtectionKeyRewrap.Rewrap(key.Xml!, current, decryptors) is { } xml)
                    {
                        key.Xml = xml;
                        rewrapped++;
                    }
                }
                catch (CryptographicException exception)
                {
                    failed++;
                    LogKeyNotDecrypted(logger, key.FriendlyName, exception);
                }
            }

            await db.SaveChangesAsync(stoppingToken);
            if (rewrapped > 0 || failed > 0)
            {
                LogRewrapped(logger, rewrapped, failed, keys.Count);
            }
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException)
        {
            LogRewrapFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DataProtection keys: {Rewrapped} re-encrypted with the current certificate, {Failed} could not be decrypted, {Total} in total")]
    private static partial void LogRewrapped(ILogger logger, int rewrapped, int failed, int total);

    [LoggerMessage(Level = LogLevel.Error, Message = "DataProtection key {FriendlyName} is encrypted with an unknown certificate: set DataProtection:PreviousCertificate")]
    private static partial void LogKeyNotDecrypted(ILogger logger, string? friendlyName, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "DataProtection keys were not re-encrypted")]
    private static partial void LogRewrapFailed(ILogger logger, Exception exception);
}
