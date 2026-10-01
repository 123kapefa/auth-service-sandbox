using System.Globalization;
using Microsoft.Extensions.Options;
using PlatformAuth.Revocation;
using StackExchange.Redis;

namespace PlatformAuth.Redis;

/// <summary>
/// Отметки об отзыве в Redis. Ключи (неймспейс auth:revoked):
/// user:{sub} = T, session:{sid} = 1, app:{azp}:{sub} = T — T в секундах Unix; живут TokenLifetime.
/// Чтение — одним MGET на три ключа.
/// </summary>
internal sealed class RedisTokenRevocationStore(IConnectionMultiplexer redis, IOptions<TokenRevocationOptions> options)
    : ITokenRevocationStore
{
    private const string PREFIX = "auth:revoked:";

    public async Task<RevocationMarks> GetAsync(
        string subject,
        string? sessionId,
        string? authorizedParty,
        CancellationToken cancellationToken)
    {
        RedisValue[] values = await redis.GetDatabase().StringGetAsync(
        [
            UserKey(subject),
            sessionId is null ? PREFIX + "session:-" : SessionKey(sessionId),
            authorizedParty is null ? PREFIX + "app:-" : AppKey(authorizedParty, subject),
        ]);

        return new RevocationMarks(Seconds(values[0]), values[1].HasValue, Seconds(values[2]));
    }

    public Task RevokeUserAsync(string subject, DateTimeOffset at, CancellationToken cancellationToken) =>
        SetAsync(UserKey(subject), at.ToUnixTimeSeconds());

    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        SetAsync(SessionKey(sessionId), 1);

    public Task RevokeAppAsync(string authorizedParty, string subject, DateTimeOffset at, CancellationToken cancellationToken) =>
        SetAsync(AppKey(authorizedParty, subject), at.ToUnixTimeSeconds());

    private Task SetAsync(RedisKey key, long value) =>
        redis.GetDatabase().StringSetAsync(key, value, options.Value.TokenLifetime);

    private static RedisKey UserKey(string subject) => PREFIX + "user:" + subject;

    private static RedisKey SessionKey(string sessionId) => PREFIX + "session:" + sessionId;

    private static RedisKey AppKey(string authorizedParty, string subject) => PREFIX + "app:" + authorizedParty + ":" + subject;

    private static long? Seconds(RedisValue value) =>
        value.HasValue && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            ? seconds
            : null;
}
