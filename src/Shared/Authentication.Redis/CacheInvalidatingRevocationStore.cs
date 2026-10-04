using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Revocation;

namespace PlatformAuth.Redis;

/// <summary>Отметка записана — свой кэш проверки по этому пользователю, сессии или роли сбрасывается сразу.</summary>
internal sealed class CacheInvalidatingRevocationStore(RedisTokenRevocationStore inner, HybridCache cache) : ITokenRevocationStore
{
    public Task<RevocationMarks> GetAsync(TokenKey key, CancellationToken cancellationToken) =>
        inner.GetAsync(key, cancellationToken);

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

    public async Task MarkAccessChangedAsync(string subject, long version, CancellationToken cancellationToken)
    {
        await inner.MarkAccessChangedAsync(subject, version, cancellationToken);
        await cache.RemoveByTagAsync(RevocationCacheTags.User(subject), cancellationToken);
    }

    public async Task MarkModeChangedAsync(string rolesClient, string role, long version, CancellationToken cancellationToken)
    {
        await inner.MarkModeChangedAsync(rolesClient, role, version, cancellationToken);
        await cache.RemoveByTagAsync(RevocationCacheTags.Mode(rolesClient, role), cancellationToken);
    }
}
