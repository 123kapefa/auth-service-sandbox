using System.Net.Http.Headers;
using AuthService.IntegrationTests.Infrastructure;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Админка на /admin: строгая CSP для её страниц и файлов; демо-страница (встроенный скрипт) — без неё.</summary>
public sealed class AdminUiTests(AuthServiceWebFactory factory) : IClassFixture<AuthServiceWebFactory>
{
    [Theory]
    [InlineData("/admin/")]
    [InlineData("/admin/users")]
    [InlineData("/admin/assets/app.js")]
    public async Task AdminPaths_StrictScriptPolicy(string path)
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        string policy = Header(response, "Content-Security-Policy");
        Assert.Contains("script-src 'self';", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task DemoPage_NoAdminPolicy()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : string.Empty;
}
