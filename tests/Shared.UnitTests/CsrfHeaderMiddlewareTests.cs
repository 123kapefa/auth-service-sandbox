using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PlatformAuth;
using PlatformAuth.Csrf;
using Xunit;

namespace Shared.UnitTests;

public sealed class CsrfHeaderMiddlewareTests
{
    [Fact]
    public async Task CustomAccessTokenCookie_IsProtectedToo()
    {
        (int status, bool reachedNext) = await Run("POST", "myAppToken=jwt", header: null, accessTokenCookie: "myAppToken");

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.False(reachedNext);
    }

    [Fact]
    public async Task CustomAccessTokenCookie_WithHeader_Passes()
    {
        (_, bool reachedNext) = await Run("POST", "myAppToken=jwt", header: "XMLHttpRequest", accessTokenCookie: "myAppToken");

        Assert.True(reachedNext);
    }

    [Fact]
    public async Task UnrelatedCookie_NotProtected()
    {
        (_, bool reachedNext) = await Run("POST", "theme=dark", header: null, accessTokenCookie: "myAppToken");

        Assert.True(reachedNext);
    }

    [Theory]
    [InlineData("__Host-accessToken=jwt")]
    [InlineData("__Host-refreshToken=r")]
    [InlineData("accessToken=jwt")]
    [InlineData("refreshToken=r")]
    public async Task PlatformCookies_Protected(string cookie)
    {
        (int status, _) = await Run("DELETE", cookie, header: null, accessTokenCookie: null);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    private static async Task<(int Status, bool ReachedNext)> Run(string method, string cookie, string? header, string? accessTokenCookie)
    {
        bool reachedNext = false;
        var middleware = new CsrfHeaderMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            Options.Create(new JwtAuthenticationOptions { AccessTokenCookie = accessTokenCookie }));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = method;
        context.Request.Headers.Cookie = cookie;
        if (header is not null)
        {
            context.Request.Headers[CsrfHeaderMiddleware.HEADER_NAME] = header;
        }

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, reachedNext);
    }
}
