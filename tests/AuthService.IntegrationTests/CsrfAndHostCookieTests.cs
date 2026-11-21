using System.Net;
using System.Text.Json;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using PlatformAuth;
using PlatformAuth.Csrf;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>
/// 12.8: меняющий запрос с cookie токена без заголовка X-Requested-With — 403; запросы с Authorization и без cookie не затронуты.
/// По https (Cookies:Secure=true) cookie с токенами читаются только под именами с приставкой __Host-.
/// </summary>
public sealed class CsrfAndHostCookieTests(AuthServiceWebFactory factory) : IClassFixture<AuthServiceWebFactory>
{
    private const string AJAX = "XMLHttpRequest";

    [Theory]
    [InlineData("/auth/refresh")]
    [InlineData("/auth/logout")]
    [InlineData("/auth/mode")]
    [InlineData("/auth/admin/roles")]
    public async Task UnsafeRequestWithTokenCookie_NoHeader_403(string path)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, path, cookies: PlainCookies(), header: null);

        await AssertCsrfRejected(response);
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task OtherUnsafeMethods_NoHeader_403(string method)
    {
        using HttpResponseMessage response = await SendAsync(new HttpMethod(method), "/auth/admin/roles/x", cookies: PlainCookies(), header: null);

        await AssertCsrfRejected(response);
    }

    [Fact]
    public async Task RefreshCookieAlone_NoHeader_403()
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/auth/refresh", cookies: $"{AuthCookies.REFRESH_TOKEN}=r", header: null);

        await AssertCsrfRejected(response);
    }

    [Fact]
    public async Task EmptyHeader_403()
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/auth/logout", cookies: PlainCookies(), header: "  ");

        await AssertCsrfRejected(response);
    }

    [Fact]
    public async Task WithHeader_PassesToEndpoint()
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/auth/logout", cookies: PlainCookies(), header: AJAX);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task SafeMethods_NoHeader_NotRejectedAsCsrf(string method)
    {
        using HttpResponseMessage response = await SendAsync(new HttpMethod(method), "/me/", cookies: PlainCookies(), header: null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizationHeader_NoCookie_NoCsrfHeaderNeeded()
    {
        using HttpClient client = factory.CreateClientFor(TestUser.AdminInAdminMode);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/logout", UriKind.Relative));

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizationHeader_WithStaleCookie_NoCsrfHeaderNeeded()
    {
        // Токен в заголовке явно выбран клиентом, cookie в браузере к запросу отношения не имеет: подделывать нечего.
        using HttpClient client = factory.CreateClientFor(TestUser.AdminInAdminMode);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/logout", UriKind.Relative));
        request.Headers.Add("Cookie", PlainCookies());

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NoTokenCookie_NoHeader_NotRejectedAsCsrf()
    {
        // Обратный вызов Keycloak и вызовы «сервис — сервису»: ни cookie, ни пользовательского заголовка нет.
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/auth/backchannel-logout", cookies: null, header: null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HttpsMode_TokenCookieUnderHostPrefixName_Authenticates()
    {
        using WebApplicationFactory<Program> https = HttpsMode();
        using HttpClient client = https.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-accessToken={factory.CreateToken(TestUser.AdminInAdminMode)}");

        HttpResponseMessage response = await client.GetAsync(new Uri("/demo/admin-only/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HttpsMode_TokenCookieWithoutPrefix_NotAccepted()
    {
        // Cookie tossing: страница на взломанном поддомене ставит accessToken с Domain=родитель — сервис её не читает.
        using WebApplicationFactory<Program> https = HttpsMode();
        using HttpClient client = https.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{AuthCookies.ACCESS_TOKEN}={factory.CreateToken(TestUser.AdminInAdminMode)}");

        HttpResponseMessage response = await client.GetAsync(new Uri("/demo/admin-only/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HttpsMode_PrefixedCookie_NoHeader_403()
    {
        using WebApplicationFactory<Program> https = HttpsMode();
        using HttpClient client = https.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/logout", UriKind.Relative));
        request.Headers.Add("Cookie", "__Host-refreshToken=r");

        using HttpResponseMessage response = await client.SendAsync(request);

        await AssertCsrfRejected(response);
    }

    private WebApplicationFactory<Program> HttpsMode() =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("Cookies:Secure", "true"));

    private string PlainCookies() => $"{AuthCookies.ACCESS_TOKEN}={factory.CreateToken(TestUser.AdminInAdminMode)}; {AuthCookies.REFRESH_TOKEN}=r";

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookies, string? header)
    {
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (cookies is not null)
        {
            request.Headers.Add("Cookie", cookies);
        }

        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation(CsrfHeaderMiddleware.HEADER_NAME, header);
        }

        return await client.SendAsync(request);
    }

    private static async Task AssertCsrfRejected(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(CsrfHeaderMiddleware.ERROR_CODE, body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString());
    }
}
