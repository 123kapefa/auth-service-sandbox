using System.Net.Http.Headers;
using AuthService.IntegrationTests.Infrastructure;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>
/// Админка на /admin и кабинет на /account: строгая CSP для их страниц и файлов; демо-страница (встроенный скрипт) — без неё.
/// </summary>
public sealed class FrontendTests(AuthServiceWebFactory factory) : IClassFixture<AuthServiceWebFactory>
{
    [Theory]
    [InlineData("/admin/")]
    [InlineData("/admin/users")]
    [InlineData("/admin/assets/app.js")]
    [InlineData("/account/")]
    [InlineData("/account/security")]
    [InlineData("/account/devices")]
    [InlineData("/account/assets/app.js")]
    public async Task FrontendPaths_StrictScriptPolicy(string path)
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        string policy = Header(response, "Content-Security-Policy");
        Assert.Contains("script-src 'self';", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task DemoPage_NoFrontendPolicy()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/dev/", UriKind.Relative));

        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : string.Empty;
}
