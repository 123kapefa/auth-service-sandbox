using System.Net;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AuthService.IntegrationTests;

public sealed class DemoPageTests(AuthServiceWebFactory factory) : IClassFixture<AuthServiceWebFactory>
{
    [Fact]
    public async Task Root_RedirectsToAccount()
    {
        using HttpClient browser = factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new Uri("/account/", UriKind.Relative), response.Headers.Location);
    }

    [Fact]
    public async Task Dev_ServesDemoPage_WithoutLogin()
    {
        using HttpClient browser = factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri("/dev/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>Auth Sandbox</title>", html, StringComparison.Ordinal);
        Assert.Contains("/auth/login?return_to=/dev/", html, StringComparison.Ordinal);
        Assert.Contains("/auth/account", html, StringComparison.Ordinal);
    }

    // Демо-страница нужна только разработчику (стенд, разработка). В бою она закрыта целиком, а модуль входа для чужих
    // страниц (/sdk/) остаётся: им пользуются приложения проекта.
    [Theory]
    [InlineData("/dev/")]
    [InlineData("/dev")]
    [InlineData("/dev/index.html")]
    [InlineData("/DEV/")]
    [InlineData("/Dev/Index.html")]
    public async Task DemoPageDisabled_NotServed(string path)
    {
        using WebApplicationFactory<Program> closed = factory.WithWebHostBuilder(builder => builder.UseSetting("DemoPage:Enabled", "false"));
        using HttpClient browser = closed.CreateClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("POST")]
    [InlineData("OPTIONS")]
    public async Task DemoPageDisabled_AnyMethod_NotServed(string method)
    {
        using WebApplicationFactory<Program> closed = factory.WithWebHostBuilder(builder => builder.UseSetting("DemoPage:Enabled", "false"));
        using HttpClient browser = closed.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri("/dev/index.html", UriKind.Relative));

        HttpResponseMessage response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DemoPageDisabled_LoginModuleStillServed()
    {
        using WebApplicationFactory<Program> closed = factory.WithWebHostBuilder(builder => builder.UseSetting("DemoPage:Enabled", "false"));
        using HttpClient browser = closed.CreateClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri("/sdk/platform-login.js", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AccountSecurity_RedirectsToOwnSecuritySection()
    {
        using HttpClient browser = factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri("/auth/account/security", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new Uri("/account/security", UriKind.Relative), response.Headers.Location);
    }

    [Theory]
    [InlineData("/auth/account?app=manager-1c", "/account/?app=manager-1c")]
    [InlineData("/auth/account/security?app=manager-1c", "/account/security?app=manager-1c")]
    [InlineData("/auth/account/devices?app=%3Cscript%3E", "/account/devices")]
    public async Task Account_FromApp_KeepsAppForBranding(string link, string expected)
    {
        using HttpClient browser = factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri(link, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new Uri(expected, UriKind.Relative), response.Headers.Location);
    }

    [Fact]
    public async Task AccountDevices_RedirectsToOwnDevicesSection()
    {
        using HttpClient browser = factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri("/auth/account/devices", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new Uri("/account/devices", UriKind.Relative), response.Headers.Location);
    }

    [Fact]
    public async Task AccountSettings_RedirectsToOwnAccount()
    {
        using HttpClient browser = factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri("/auth/account", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new Uri("/account/", UriKind.Relative), response.Headers.Location);
    }
}
