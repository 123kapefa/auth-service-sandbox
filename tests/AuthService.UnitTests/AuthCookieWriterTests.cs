using AuthService.Core.Abstractions;
using AuthService.Core.Features.Auth.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using PlatformAuth;
using Xunit;
using SameSiteMode = Microsoft.Net.Http.Headers.SameSiteMode;

namespace AuthService.UnitTests;

public sealed class AuthCookieWriterTests
{
    private static readonly DateTimeOffset _accessExpires = new(2026, 9, 26, 12, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _refreshExpires = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly TokenSet _tokens = new("access-jwt", _accessExpires, "refresh-token", _refreshExpires);

    [Fact]
    public void SetTokens_ValuesAndLifetimeOfTokens()
    {
        IList<SetCookieHeaderValue> cookies = SetTokens(new AuthCookieOptions());

        SetCookieHeaderValue access = Cookie(cookies, AuthCookies.AccessToken(hostPrefix: true));
        Assert.Equal("access-jwt", access.Value.ToString());
        Assert.Equal(_accessExpires, access.Expires);
        AssertProtected(access);

        SetCookieHeaderValue refresh = Cookie(cookies, AuthCookies.RefreshToken(hostPrefix: true));
        Assert.Equal("refresh-token", refresh.Value.ToString());
        Assert.Equal(_refreshExpires, refresh.Expires);
        AssertProtected(refresh);
    }

    [Fact]
    public void SetTokens_HostOnly_NoDomainSoSubdomainsNeverGetTokens()
    {
        IList<SetCookieHeaderValue> cookies = SetTokens(new AuthCookieOptions { Secure = false });

        Assert.Equal(2, cookies.Count);
        Assert.All(cookies, cookie => Assert.False(cookie.Domain.HasValue));
    }

    [Fact]
    public void LocalHttp_NotSecure()
    {
        IList<SetCookieHeaderValue> cookies = SetTokens(new AuthCookieOptions { Secure = false });

        Assert.All(cookies, cookie => Assert.False(cookie.Secure));
    }

    [Fact]
    public void Clear_ExpiresBothWithSamePathAndNoDomain()
    {
        var context = new DefaultHttpContext();
        new AuthCookieWriter(Options.Create(new AuthCookieOptions { Secure = false })).Clear(context.Response);

        IList<SetCookieHeaderValue> cookies = SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie);
        Assert.Equal("/", Cookie(cookies, AuthCookies.ACCESS_TOKEN).Path.ToString());
        Assert.Equal("/auth/", Cookie(cookies, AuthCookies.REFRESH_TOKEN).Path.ToString());
        Assert.All(cookies, cookie =>
        {
            Assert.False(cookie.Domain.HasValue);
            Assert.True(cookie.Expires < DateTimeOffset.UtcNow);
        });
    }

    // Cookie с приставкой __Host-: браузер примет её только с Secure, Path=/ и без Domain — подменить с соседнего поддомена нельзя.
    [Fact]
    public void Https_HostPrefixNames_PathRoot_NoDomain()
    {
        IList<SetCookieHeaderValue> cookies = SetTokens(new AuthCookieOptions { Secure = true });

        // Новые имена ставятся, старые (accessToken, refreshToken без приставки) снимаются при входе.
        IList<SetCookieHeaderValue> set = [.. cookies.Where(cookie => cookie.Name.ToString().StartsWith("__Host-", StringComparison.Ordinal))];
        Assert.Equal(["__Host-accessToken", "__Host-refreshToken"], set.Select(cookie => cookie.Name.ToString()).Order(StringComparer.Ordinal));
        Assert.All(cookies.Except(set), cookie => Assert.True(cookie.Expires < DateTimeOffset.UtcNow));
        Assert.All(set, cookie =>
        {
            Assert.Equal("/", cookie.Path.ToString());
            Assert.True(cookie.Secure);
            Assert.False(cookie.Domain.HasValue);
            AssertProtected(cookie);
        });
    }

    [Fact]
    public void LocalHttp_PlainNames_RefreshOnlyForAuthEndpoints()
    {
        IList<SetCookieHeaderValue> cookies = SetTokens(new AuthCookieOptions { Secure = false });

        Assert.Equal("/", Cookie(cookies, AuthCookies.ACCESS_TOKEN).Path.ToString());
        Assert.Equal("/auth/", Cookie(cookies, AuthCookies.REFRESH_TOKEN).Path.ToString());
    }

    [Fact]
    public void Https_Clear_ExpiresPrefixedAndLegacyNames()
    {
        var context = new DefaultHttpContext();
        new AuthCookieWriter(Options.Create(new AuthCookieOptions { Secure = true })).Clear(context.Response);

        IList<SetCookieHeaderValue> cookies = SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie);
        Assert.Equal("/", Cookie(cookies, "__Host-accessToken").Path.ToString());
        Assert.Equal("/", Cookie(cookies, "__Host-refreshToken").Path.ToString());
        Assert.Equal("/auth/", Cookie(cookies, AuthCookies.REFRESH_TOKEN).Path.ToString());
        Assert.All(cookies, cookie => Assert.True(cookie.Expires < DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Https_ReadsOnlyPrefixedRefreshCookie()
    {
        var writer = new AuthCookieWriter(Options.Create(new AuthCookieOptions { Secure = true }));
        var plain = new DefaultHttpContext();
        plain.Request.Headers.Cookie = $"{AuthCookies.REFRESH_TOKEN}=tossed";
        var prefixed = new DefaultHttpContext();
        prefixed.Request.Headers.Cookie = "__Host-refreshToken=real";

        Assert.Null(writer.ReadRefreshToken(plain.Request));
        Assert.Equal("real", writer.ReadRefreshToken(prefixed.Request));
    }

    private static IList<SetCookieHeaderValue> SetTokens(AuthCookieOptions options)
    {
        var context = new DefaultHttpContext();
        new AuthCookieWriter(Options.Create(options)).SetTokens(context.Response, _tokens);
        return SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie);
    }

    private static SetCookieHeaderValue Cookie(IList<SetCookieHeaderValue> cookies, string name) =>
        Assert.Single(cookies, cookie => cookie.Name == name);

    private static void AssertProtected(SetCookieHeaderValue cookie)
    {
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
    }
}
