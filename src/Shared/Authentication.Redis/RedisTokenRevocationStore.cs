using System.Globalization;
using Microsoft.Extensions.Options;
using PlatformAuth.Revocation;
using StackExchange.Redis;

namespace PlatformAuth.Redis;

/// <summary>
/// Отметки в Redis. Отзыв (неймспейс auth:revoked): user:{sub} = T, session:{sid} = 1, app:{приложение}:{sub} = T —
/// приложение — клиент входа (azp) или клиент ролей (audience сервиса: у «Менеджера 1С» входит другой клиент) —
/// T в секундах Unix. Доступ изменился (неймспейс auth:changed): user:{sub} = версия, mode:{realm}:{клиент}:{роль} =
/// версия. Все живут TokenLifetime: дольше старые токены не живут сами. Чтение — одним MGET.
/// Redis может быть общим у нескольких проектов (realm): ключи с sub и так уникальны, ключ роли — с realm проекта
/// (клиенты в проектах называются одинаково — auth-api).
/// </summary>
internal sealed class RedisTokenRevocationStore(
    IConnectionMultiplexer redis,
    IOptions<TokenRevocationOptions> options,
    IOptions<JwtAuthenticationOptions> authentication)
    : ITokenRevocationStore
{
    private const string PREFIX = "auth:revoked:";
    private const string CHANGED = "auth:changed:";

    public async Task<RevocationMarks> GetAsync(TokenKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        RedisValue[] values = await redis.GetDatabase().StringGetAsync(
        [
            UserKey(key.Subject),
            key.SessionId is null ? PREFIX + "session:-" : SessionKey(key.SessionId),
            key.AuthorizedParty is null ? PREFIX + "app:-" : AppKey(key.AuthorizedParty, key.Subject),
            AccessKey(key.Subject),
            key.RolesClient is null || key.ActiveMode is null ? CHANGED + "mode:-" : ModeKey(key.RolesClient, key.ActiveMode),
            key.RolesClient is null ? PREFIX + "app:-" : AppKey(key.RolesClient, key.Subject),
        ]);

        return new RevocationMarks(
            Number(values[0]), values[1].HasValue, Latest(Number(values[2]), Number(values[5])), Number(values[3]), Number(values[4]));
    }

    public Task RevokeUserAsync(string subject, DateTimeOffset at, CancellationToken cancellationToken) =>
        SetAsync(UserKey(subject), at.ToUnixTimeSeconds());

    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        SetAsync(SessionKey(sessionId), 1);

    public Task RevokeAppAsync(string authorizedParty, string subject, DateTimeOffset at, CancellationToken cancellationToken) =>
        SetAsync(AppKey(authorizedParty, subject), at.ToUnixTimeSeconds());

    public Task MarkAccessChangedAsync(string subject, long version, CancellationToken cancellationToken) =>
        SetAsync(AccessKey(subject), version);

    public Task MarkModeChangedAsync(string rolesClient, string role, long version, CancellationToken cancellationToken) =>
        SetAsync(ModeKey(rolesClient, role), version);

    private Task SetAsync(RedisKey key, long value) =>
        redis.GetDatabase().StringSetAsync(key, value, options.Value.TokenLifetime);

    private static RedisKey UserKey(string subject) => PREFIX + "user:" + subject;

    private static RedisKey SessionKey(string sessionId) => PREFIX + "session:" + sessionId;

    private static RedisKey AppKey(string authorizedParty, string subject) => PREFIX + "app:" + authorizedParty + ":" + subject;

    private static RedisKey AccessKey(string subject) => CHANGED + "user:" + subject;

    private RedisKey ModeKey(string rolesClient, string role) => CHANGED + "mode:" + Realm + ":" + rolesClient + ":" + role;

    // Realm проекта — последний сегмент издателя токенов (…/realms/shop → shop).
    private string Realm => authentication.Value.ResolveIssuer().TrimEnd('/').Split('/')[^1];

    private static long? Latest(long? first, long? second) =>
        first is null ? second : second is null ? first : Math.Max(first.Value, second.Value);

    private static long? Number(RedisValue value) =>
        value.HasValue && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
            ? number
            : null;
}
