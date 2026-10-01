using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlatformAuth.Revocation;

namespace PlatformAuth.Redis;

/// <summary>
/// Отозван ли токен: отметки из хранилища (Redis) через короткий кэш в памяти — HybridCache только L1, без второго
/// уровня: решение о доступе не должно лежать в распределённом кэше (стандарт платформы — Redis напрямую + burst L1).
/// Redis недоступен — токен пропускается (он и так живёт не дольше 15 минут), с предупреждением в лог.
/// </summary>
internal sealed partial class TokenRevocationChecker(
    ITokenRevocationStore store,
    HybridCache cache,
    IOptions<TokenRevocationOptions> options,
    ILogger<TokenRevocationChecker> logger) : ITokenRevocationChecker
{
    public async Task<bool> IsRevokedAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string? subject = principal.FindFirstValue(AuthClaims.SUBJECT);
        if (subject is null
            || !long.TryParse(principal.FindFirstValue(AuthClaims.ISSUED_AT), NumberStyles.Integer, CultureInfo.InvariantCulture, out long issuedAt))
        {
            return false; // без sub или iat отзывать нечего — такие токены отсечёт проверка подписи и срока
        }

        string? sessionId = principal.FindFirstValue(AuthClaims.SESSION_ID);
        string? authorizedParty = principal.FindFirstValue(AuthClaims.AUTHORIZED_PARTY);

        try
        {
            TimeSpan local = options.Value.LocalCache;
            RevocationMarks marks = local <= TimeSpan.Zero
                ? await store.GetAsync(subject, sessionId, authorizedParty, cancellationToken)
                : await cache.GetOrCreateAsync(
                    $"auth:revocation:{subject}:{sessionId}:{authorizedParty}",
                    (store, subject, sessionId, authorizedParty),
                    static (state, token) => new ValueTask<RevocationMarks>(
                        state.store.GetAsync(state.subject, state.sessionId, state.authorizedParty, token)),
                    new HybridCacheEntryOptions
                    {
                        Expiration = local,
                        LocalCacheExpiration = local,
                        Flags = HybridCacheEntryFlags.DisableDistributedCache,
                    },
                    RevocationCacheTags.For(subject, sessionId),
                    cancellationToken);

            return marks.Revokes(issuedAt);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogStoreUnavailable(logger, exception);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token revocation store is unavailable; the token is accepted")]
    private static partial void LogStoreUnavailable(ILogger logger, Exception exception);
}
