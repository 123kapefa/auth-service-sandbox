using System.Globalization;
using System.Security.Claims;
using AuthService.InfrastructureTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth;
using PlatformAuth.Revocation;
using StackExchange.Redis;
using Xunit;

namespace AuthService.InfrastructureTests;

/// <summary>Отметки мгновенного отзыва и «доступ изменился» в настоящем Redis — запись, чтение одним MGET, срок жизни.</summary>
[Collection(InfrastructureCollection.NAME)]
[Trait("Category", TestCategories.INFRASTRUCTURE)]
public sealed class RedisRevocationTests(InfrastructureFixture fixture) : IAsyncLifetime
{
    private const string SUBJECT = "6f1c2d3e-0000-4000-8000-000000000001";
    private static readonly DateTimeOffset s_now = DateTimeOffset.UtcNow;

    private ITokenRevocationStore Store => fixture.Services.GetRequiredService<ITokenRevocationStore>();

    private ITokenRevocationChecker Checker => fixture.Services.GetRequiredService<ITokenRevocationChecker>();

    public Task InitializeAsync() => fixture.ResetRedisAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task NoMarks_None()
    {
        RevocationMarks marks = await Store.GetAsync(new TokenKey(SUBJECT, "session-1", "sandbox-bff", "sandbox-api", "customer"), CancellationToken.None);

        Assert.Equal(RevocationMarks.None, marks);
    }

    [Fact]
    public async Task AllMarks_ReadTogether()
    {
        await Store.RevokeUserAsync(SUBJECT, s_now, CancellationToken.None);
        await Store.RevokeSessionAsync("session-1", CancellationToken.None);
        await Store.RevokeAppAsync("sandbox-bff", SUBJECT, s_now.AddSeconds(-10), CancellationToken.None);
        await Store.MarkAccessChangedAsync(SUBJECT, 7, CancellationToken.None);
        await Store.MarkModeChangedAsync("sandbox-api", "manager", 3, CancellationToken.None);

        RevocationMarks marks = await Store.GetAsync(new TokenKey(SUBJECT, "session-1", "sandbox-bff", "sandbox-api", "manager"), CancellationToken.None);

        Assert.Equal(s_now.ToUnixTimeSeconds(), marks.UserRevokedAt);
        Assert.True(marks.SessionRevoked);
        Assert.Equal(s_now.AddSeconds(-10).ToUnixTimeSeconds(), marks.AppRevokedAt);
        Assert.Equal(7, marks.AccessVersion);
        Assert.Equal(3, marks.ModeVersion);
    }

    [Fact]
    public async Task AppRevoked_ByRolesClient_LatestWins()
    {
        // Отзыв доступа к приложению — по клиенту входа (azp) или по клиенту ролей (aud сервиса): берётся более поздний.
        await Store.RevokeAppAsync("sandbox-bff", SUBJECT, s_now.AddSeconds(-30), CancellationToken.None);
        await Store.RevokeAppAsync("sandbox-api", SUBJECT, s_now, CancellationToken.None);

        RevocationMarks marks = await Store.GetAsync(new TokenKey(SUBJECT, null, "sandbox-bff", "sandbox-api", null), CancellationToken.None);

        Assert.Equal(s_now.ToUnixTimeSeconds(), marks.AppRevokedAt);
        Assert.False(marks.SessionRevoked);
    }

    [Fact]
    public async Task Marks_OtherSubjectAndSession_NotAffected()
    {
        await Store.RevokeUserAsync(SUBJECT, s_now, CancellationToken.None);
        await Store.RevokeSessionAsync("session-1", CancellationToken.None);

        RevocationMarks marks = await Store.GetAsync(new TokenKey("someone-else", "session-2", null, null, null), CancellationToken.None);

        Assert.Null(marks.UserRevokedAt);
        Assert.False(marks.SessionRevoked);
    }

    [Fact]
    public async Task ModeMark_KeyedByRealm_ExpiresWithTokens()
    {
        await Store.MarkModeChangedAsync("sandbox-api", "manager", 5, CancellationToken.None);
        await Store.RevokeUserAsync(SUBJECT, s_now, CancellationToken.None);

        IDatabase redis = fixture.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        // Realm — из издателя токенов (…/realms/sandbox): у проектов на общем Redis клиенты называются одинаково.
        Assert.Equal(5, (long)await redis.StringGetAsync("auth:changed:mode:sandbox:sandbox-api:manager"));
        TimeSpan? ttl = await redis.KeyTimeToLiveAsync($"auth:revoked:user:{SUBJECT}");
        Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(16));
    }

    [Fact]
    public async Task Checker_TokenIssuedBeforeRevocation_Revoked()
    {
        await Store.RevokeUserAsync(SUBJECT, s_now, CancellationToken.None);

        Assert.Equal(TokenStatus.Revoked, await Checker.CheckAsync(Token(s_now.AddMinutes(-1)), CancellationToken.None));
        Assert.Equal(TokenStatus.Valid, await Checker.CheckAsync(Token(s_now.AddSeconds(1)), CancellationToken.None));
    }

    [Fact]
    public async Task Checker_AccessVersionBehind_AccessChanged()
    {
        await Store.MarkAccessChangedAsync(SUBJECT, 4, CancellationToken.None);

        Assert.Equal(TokenStatus.AccessChanged, await Checker.CheckAsync(Token(s_now, accessVersion: 3), CancellationToken.None));
        Assert.Equal(TokenStatus.Valid, await Checker.CheckAsync(Token(s_now, accessVersion: 4), CancellationToken.None));
    }

    [Fact]
    public async Task Checker_SessionRevoked_Revoked()
    {
        await Store.RevokeSessionAsync("session-1", CancellationToken.None);

        Assert.Equal(TokenStatus.Revoked, await Checker.CheckAsync(Token(s_now, session: "session-1"), CancellationToken.None));
        Assert.Equal(TokenStatus.Valid, await Checker.CheckAsync(Token(s_now, session: "session-2"), CancellationToken.None));
    }

    private static ClaimsPrincipal Token(DateTimeOffset issuedAt, string session = "session-0", long? accessVersion = null)
    {
        List<Claim> claims =
        [
            new(AuthClaims.SUBJECT, SUBJECT),
            new(AuthClaims.ISSUED_AT, issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new(AuthClaims.SESSION_ID, session),
            new(AuthClaims.AUTHORIZED_PARTY, "sandbox-bff"),
        ];
        if (accessVersion is { } version)
        {
            claims.Add(new Claim(AuthClaims.ACCESS_VERSION, version.ToString(CultureInfo.InvariantCulture)));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
