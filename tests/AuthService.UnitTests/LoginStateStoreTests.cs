using AuthService.Core.Features.Auth.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace AuthService.UnitTests;

public sealed class LoginStateStoreTests
{
    private static readonly LoginState _state = new("state-1", "verifier-1", "https://shop.example.com/cart");

    private readonly LoginStateStore _store = new(
        new EphemeralDataProtectionProvider(),
        Options.Create(new AuthCookieOptions()),
        TimeProvider.System);

    [Fact]
    public void Saved_IsEncryptedLaxCookieUnderAuthPath()
    {
        SetCookieHeaderValue cookie = Save();

        Assert.Equal(LoginStateStore.COOKIE_NAME, cookie.Name.ToString());
        Assert.DoesNotContain("verifier-1", cookie.Value.ToString(), StringComparison.Ordinal);
        Assert.Equal("/auth/", cookie.Path.ToString());
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
        Assert.True(cookie.HttpOnly);
    }

    [Fact]
    public void Take_ReturnsSavedState_AndDeletesCookie()
    {
        string payload = Save().Value.ToString();
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{LoginStateStore.COOKIE_NAME}={payload}";

        LoginState? state = _store.Take(context.Request, context.Response);

        Assert.Equal(_state, state);
        SetCookieHeaderValue deletion = Assert.Single(SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie));
        Assert.True(deletion.Expires < DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tampered-value")]
    public void Take_MissingOrTampered_ReturnsNull(string? payload)
    {
        var context = new DefaultHttpContext();
        if (payload is not null)
        {
            context.Request.Headers.Cookie = $"{LoginStateStore.COOKIE_NAME}={payload}";
        }

        Assert.Null(_store.Take(context.Request, context.Response));
    }

    [Fact]
    public void Take_FromAnotherKey_ReturnsNull()
    {
        string payload = Save().Value.ToString();
        var otherStore = new LoginStateStore(new EphemeralDataProtectionProvider(), Options.Create(new AuthCookieOptions()), TimeProvider.System);
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{LoginStateStore.COOKIE_NAME}={payload}";

        Assert.Null(otherStore.Take(context.Request, context.Response));
    }

    private SetCookieHeaderValue Save()
    {
        var context = new DefaultHttpContext();
        _store.Save(context.Response, _state);
        return Assert.Single(SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie));
    }
}
