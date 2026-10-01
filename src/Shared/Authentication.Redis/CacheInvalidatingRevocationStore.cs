using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Revocation;

namespace PlatformAuth.Redis;

/// <summary>Отметка записана — свой кэш проверки по этому пользователю или сессии сбрасывается сразу.</summary>
internal sealed class CacheInvalidatingRevocationStore(RedisTokenRevocationStore inner, HybridCache cache) : ITokenRevocationStore
{
    public Task<RevocationMarks> GetAsync(string subject, string? sessionId, string? authorizedParty, CancellationToken cancellationToken) =>
        inner.GetAsync(subject, sessionId, authorizedParty, cancellationToken);

    public async Task RevokeUserAsync(string subject, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await inner.RevokeUserAsync(subject, at, cancellationToken);
        await cache.RemoveByTagAsync(RevocationCacheTags.User(subject), cancellationToken);
    }

    public async Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        await inner.RevokeSessionAsync(sessionId, cancellationToken);
        await cache.RemoveByTagAsync(RevocationCacheTags.Session(sessionId), cancellationToken);
    }

    public async Task RevokeAppAsync(string authorizedParty, string subject, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await inner.RevokeAppAsync(authorizedParty, subject, at, cancellationToken);
        await cache.RemoveByTagAsync(RevocationCacheTags.User(subject), cancellationToken);
    }
}
