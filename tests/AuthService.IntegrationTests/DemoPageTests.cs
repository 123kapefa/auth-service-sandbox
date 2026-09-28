using System.Net;
using AuthService.IntegrationTests.Infrastructure;
using Xunit;

namespace AuthService.IntegrationTests;

public sealed class DemoPageTests(AuthServiceWebFactory factory) : IClassFixture<AuthServiceWebFactory>
{
    [Fact]
    public async Task Root_ServesDemoPage_WithoutLogin()
    {
        using HttpClient browser = factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>Auth Sandbox</title>", html, StringComparison.Ordinal);
        Assert.Contains("/auth/login?return_to=/", html, StringComparison.Ordinal);
    }
}
