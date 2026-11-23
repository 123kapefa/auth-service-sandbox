using System.Net;
using System.Text.Json;
using AuthService.Api.Configuration;
using AuthService.Core.Authorization;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>
/// 13.4: лимиты частоты на /auth/login, /auth/refresh и /auth/services/*. Превышение — 429 с Retry-After и обычным телом ошибки; лимит
/// считается по IP клиента (за прокси — по X-Forwarded-For) и по ключу сервиса; остальные адреса и чужие ключи не задеты.
/// </summary>
public sealed class RateLimitingTests : IClassFixture<AuthServiceWebFactory>, IDisposable
{
    private const string LOGIN = "/auth/login?return_to=/me/";
    private static readonly Guid _serviceAccount = Guid.NewGuid();

    private readonly AuthServiceWebFactory _factory;

    public RateLimitingTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _factory.IdentityAdmin.Reset();
        _factory.IdentityAdmin.SeedAppRoles();
        _factory.Revocations.Reset();
    }

    [Fact]
    public async Task Login_OverLimit_429_WithRetryAfter_AndErrorBody()
    {
        using HttpClient client = Limited(("Login:PermitLimit", "3"));

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Redirect, (await Get(client, LOGIN)).StatusCode);
        }

        using HttpResponseMessage rejected = await Get(client, LOGIN);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        int retryAfter = int.Parse(rejected.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 60);
        using JsonDocument body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("isError").GetBoolean());
        JsonElement error = body.RootElement.GetProperty("error");
        Assert.Equal("RATE_LIMIT", error.GetProperty("type").GetString());
        Assert.Equal("auth.rate_limited", error.GetProperty("messages")[0].GetProperty("code").GetString());
        Assert.Contains($"{retryAfter}", error.GetProperty("messages")[0].GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/AUTH/LOGIN?return_to=/me/")]
    [InlineData("/auth/login/?return_to=/me/")]
    [InlineData("//auth/login?return_to=/me/")]
    [InlineData("/auth/./login?return_to=/me/")]
    public async Task Login_PathVariants_DoNotEscapeTheLimit(string variant)
    {
        using HttpClient client = Limited(("Login:PermitLimit", "1"));
        await Get(client, LOGIN);

        // Вариант записи пути либо не доходит до входа (404), либо считается тем же лимитом (429): редиректа на вход быть не должно.
        HttpStatusCode status = (await Get(client, variant)).StatusCode;

        Assert.NotEqual(HttpStatusCode.Redirect, status);
    }

    [Fact]
    public async Task Login_LimitIsPerClientIp()
    {
        using HttpClient client = Limited(("Login:PermitLimit", "2"));

        await Get(client, LOGIN, ip: "203.0.113.1");
        await Get(client, LOGIN, ip: "203.0.113.1");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Get(client, LOGIN, ip: "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Get(client, LOGIN, ip: "203.0.113.2")).StatusCode);
    }

    [Fact]
    public async Task BehindProxy_LimitIsByForwardedClientIp_NotByProxy()
    {
        using HttpClient client = Limited(("Login:PermitLimit", "2"));

        // Все запросы приходят от прокси (127.0.0.1 — доверенная сеть), клиенты разные.
        await Get(client, LOGIN, forwardedFor: "198.51.100.7");
        await Get(client, LOGIN, forwardedFor: "198.51.100.7");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Get(client, LOGIN, forwardedFor: "198.51.100.7")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Get(client, LOGIN, forwardedFor: "198.51.100.8")).StatusCode);
    }

    [Fact]
    public async Task UntrustedClient_CannotChooseOwnIp_ByForwardedHeader()
    {
        using HttpClient client = Limited(("Login:PermitLimit", "2"));

        // Клиент не из доверенной сети: X-Forwarded-For игнорируется, подменой адреса лимит не обойти.
        for (int i = 0; i < 2; i++)
        {
            await Get(client, LOGIN, ip: "203.0.113.50", forwardedFor: $"198.51.100.{i}");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Get(client, LOGIN, ip: "203.0.113.50", forwardedFor: "198.51.100.99")).StatusCode);
    }

    [Fact]
    public async Task Refresh_OverLimit_429()
    {
        using HttpClient client = Limited(("Refresh:PermitLimit", "2"));

        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/auth/refresh")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(client, "/auth/refresh")).StatusCode);
        // Вход — отдельный лимит.
        Assert.Equal(HttpStatusCode.Redirect, (await Get(client, LOGIN)).StatusCode);
    }

    [Fact]
    public async Task Services_LimitIsPerServiceKey()
    {
        using HttpClient orders = LimitedService("service-orders", ("Services:PermitLimit", "3"));
        using HttpClient shop = LimitedService("service-shop", ("Services:PermitLimit", "3"));
        string path = $"/auth/services/users/{Guid.NewGuid()}";

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Get(orders, path)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Get(orders, path)).StatusCode);
        // Другой сервис с того же адреса — со своим лимитом.
        Assert.Equal(HttpStatusCode.NotFound, (await Get(shop, path)).StatusCode);
    }

    [Fact]
    public async Task Services_WithoutServiceToken_CountedByIp_AndDoNotTouchServiceKeys()
    {
        using HttpClient anonymous = Limited(("ServicesAnonymous:PermitLimit", "2"), ("Services:PermitLimit", "100"));
        string path = $"/auth/services/users/{Guid.NewGuid()}";

        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Get(anonymous, path)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Get(anonymous, path)).StatusCode);

        // Сервис с настоящим токеном с того же адреса не страдает от чужого мусора.
        using HttpClient service = LimitedService("service-orders", ("ServicesAnonymous:PermitLimit", "2"), ("Services:PermitLimit", "100"));
        Assert.Equal(HttpStatusCode.NotFound, (await Get(service, path)).StatusCode);
    }

    [Fact]
    public async Task OtherEndpoints_AreNotLimited()
    {
        using HttpClient client = Limited(("Login:PermitLimit", "1"), ("Refresh:PermitLimit", "1"), ("ServicesAnonymous:PermitLimit", "1"));

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Get(client, "/me/")).StatusCode);
        }
    }

    [Fact]
    public async Task ZeroPermitLimit_DisablesPolicy_AndDisabledSwitchDisablesAll()
    {
        using HttpClient noLimit = Limited(("Login:PermitLimit", "0"));
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Redirect, (await Get(noLimit, LOGIN)).StatusCode);
        }

        using HttpClient off = Limited(("Enabled", "false"), ("Login:PermitLimit", "1"));
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Redirect, (await Get(off, LOGIN)).StatusCode);
        }
    }

    [Theory]
    [InlineData("-1", "60")]
    [InlineData("10", "0")]
    [InlineData("10", "100000")]
    public void WrongSettings_StopStartup(string permit, string window)
    {
        var options = new RateLimitingOptions { Login = new RateLimitRule { PermitLimit = int.Parse(permit, System.Globalization.CultureInfo.InvariantCulture), WindowSeconds = int.Parse(window, System.Globalization.CultureInfo.InvariantCulture) } };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("203.0.113.9", "203.0.113.9")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "20010db800010002::/64")]
    [InlineData("2001:db8:1:2:1111:2222:3333:4444", "20010db800010002::/64")]
    public void ClientKey_NormalizesAddress(string address, string expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        Assert.Equal(expected, RateLimitingConfiguration.ClientKey(context));
    }

    public void Dispose()
    {
        foreach (Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> server in _servers.Values)
        {
            server.Dispose();
        }
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string path, string? ip = null, string? forwardedFor = null) =>
        client.SendAsync(Request(HttpMethod.Get, path, ip, forwardedFor));

    private static Task<HttpResponseMessage> Post(HttpClient client, string path) =>
        client.SendAsync(Request(HttpMethod.Post, path, null, null));

    private static HttpRequestMessage Request(HttpMethod method, string path, string? ip, string? forwardedFor)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        if (ip is not null)
        {
            request.Headers.Add(TestClientIp.HEADER, ip);
        }

        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return request;
    }

    private HttpClient Limited(params (string Key, string Value)[] settings) => Build(settings).CreateClient(NoRedirects());

    private HttpClient LimitedService(string clientId, params (string Key, string Value)[] settings)
    {
        HttpClient client = Build(settings).CreateClient(NoRedirects());
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", _factory.CreateServiceToken(clientId, _serviceAccount, AuthServicePermissions.ServiceAccess.USERS_READ));
        return client;
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions NoRedirects() => new() { AllowAutoRedirect = false };

    // Один и тот же сервер для клиентов теста: лимитер живёт в сервере, поэтому клиенты «orders» и «shop» делят его.
    private readonly Dictionary<string, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>> _servers = [];

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Build((string Key, string Value)[] settings)
    {
        string key = string.Join(';', settings.Select(setting => $"{setting.Key}={setting.Value}"));
        if (_servers.TryGetValue(key, out Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? existing))
        {
            return existing;
        }

        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> server = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Enabled", "true");
            foreach ((string settingKey, string value) in settings)
            {
                builder.UseSetting($"RateLimiting:{settingKey}", value);
            }

            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, TestClientIp>());
        });
        _servers[key] = server;
        return server;
    }

    /// <summary>Адрес клиента из заголовка X-Test-Ip (у тестового сервера адреса нет); без заголовка — 127.0.0.1, как прокси на той же машине.</summary>
    private sealed class TestClientIp : IStartupFilter
    {
        public const string HEADER = "X-Test-Ip";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = context.Request.Headers.TryGetValue(HEADER, out Microsoft.Extensions.Primitives.StringValues value)
                        ? IPAddress.Parse(value.ToString())
                        : IPAddress.Loopback;
                    return nextMiddleware(context);
                });
                next(app);
            };
    }
}
