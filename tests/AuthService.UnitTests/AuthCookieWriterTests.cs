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
    public void SetTokens_AccessForAllServices_RefreshOnlyForAuthEndpoints()
    {
        IList<SetCookieHeaderValue> cookies = SetTokens(new AuthCookieOptions());

        SetCookieHeaderValue access = Cookie(cookies, AuthCookies.ACCESS_TOKEN);
        Assert.Equal("access-jwt", access.Value.ToString());
        Assert.Equal("/", access.Path.ToString());
        Assert.Equal(_accessExpires, access.Expires);
        AssertProtected(access);

        SetCookieHeaderValue refresh = Cookie(cookies, AuthCookies.REFRESH_TOKEN);
        Assert.Equal("refresh-token", refresh.Value.ToString());
        Assert.Equal("/auth/", refresh.Path.ToString());
        Assert.Equal(_refreshExpires, refresh.Expires);
        AssertProtected(refresh);
    }

    [Fact]
    public void SetTokens_HostOnly_NoDomainSoSubdomainsNeverGetTokens()
    {
        IList<SetCookieHeaderValue> cookies = SetTokens(new AuthCookieOptions());

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
        new AuthCookieWriter(Options.Create(new AuthCookieOptions())).Clear(context.Response);

        IList<SetCookieHeaderValue> cookies = SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie);
        Assert.Equal("/", Cookie(cookies, AuthCookies.ACCESS_TOKEN).Path.ToString());
        Assert.Equal("/auth/", Cookie(cookies, AuthCookies.REFRESH_TOKEN).Path.ToString());
        Assert.All(cookies, cookie =>
        {
            Assert.False(cookie.Domain.HasValue);
            Assert.True(cookie.Expires < DateTimeOffset.UtcNow);
        });
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
