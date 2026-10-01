using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlatformAuth.Revocation;

namespace PlatformAuth.Redis;

/// <summary>
/// Отозван ли токен и не изменился ли доступ: отметки из хранилища (Redis) через короткий кэш в памяти — HybridCache
/// только L1, без второго уровня: решение о доступе не должно лежать в распределённом кэше (стандарт платформы —
/// Redis напрямую + burst L1). Redis недоступен — токен пропускается (он и так живёт не дольше 15 минут),
/// с предупреждением в лог.
/// </summary>
internal sealed partial class TokenRevocationChecker(
    ITokenRevocationStore store,
    HybridCache cache,
    IOptions<TokenRevocationOptions> options,
    IOptions<JwtAuthenticationOptions> authentication,
    ILogger<TokenRevocationChecker> logger) : ITokenRevocationChecker
{
    public async Task<TokenStatus> CheckAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string? subject = principal.FindFirstValue(AuthClaims.SUBJECT);
        if (subject is null || Number(principal, AuthClaims.ISSUED_AT) is not { } issuedAt)
        {
            return TokenStatus.Valid; // без sub или iat отзывать нечего — такие токены отсечёт проверка подписи и срока
        }

        var key = new TokenKey(
            subject,
            principal.FindFirstValue(AuthClaims.SESSION_ID),
            principal.FindFirstValue(AuthClaims.AUTHORIZED_PARTY),
            authentication.Value.Audience,
            principal.FindFirstValue(AuthClaims.ACTIVE_MODE));

        try
        {
            TimeSpan local = options.Value.LocalCache;
            RevocationMarks marks = local <= TimeSpan.Zero
                ? await store.GetAsync(key, cancellationToken)
                : await cache.GetOrCreateAsync(
                    $"auth:revocation:{key.Subject}:{key.SessionId}:{key.AuthorizedParty}:{key.ActiveMode}",
                    (store, key),
                    static (state, token) => new ValueTask<RevocationMarks>(state.store.GetAsync(state.key, token)),
                    new HybridCacheEntryOptions
                    {
                        Expiration = local,
                        LocalCacheExpiration = local,
                        Flags = HybridCacheEntryFlags.DisableDistributedCache,
                    },
                    RevocationCacheTags.For(key),
                    cancellationToken);

            if (marks.Revokes(issuedAt))
            {
                return TokenStatus.Revoked;
            }

            return marks.AccessChanged(Number(principal, AuthClaims.ACCESS_VERSION), Number(principal, AuthClaims.MODE_VERSION))
                ? TokenStatus.AccessChanged
                : TokenStatus.Valid;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogStoreUnavailable(logger, exception);
            return TokenStatus.Valid;
        }
    }

    private static long? Number(ClaimsPrincipal principal, string claim) =>
        long.TryParse(principal.FindFirstValue(claim), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token revocation store is unavailable; the token is accepted")]
    private static partial void LogStoreUnavailable(ILogger logger, Exception exception);
}
