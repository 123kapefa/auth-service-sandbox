using System.Net;
using System.Text;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Infrastructure.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth;
using SharedKernel;
using Xunit;

namespace AuthService.UnitTests;

// Плагин Keycloak platform-codes подменён HTTP-заглушкой: какие запросы уходят и как разбираются ответы.
public sealed class KeycloakCodesTests : IDisposable
{
    private const string REALM = "http://keycloak:8180/realms/shop";
    private const string CODES = $"{REALM}/platform-codes";

    private const string SETTINGS_BODY = """
        {
          "phoneChannels": ["call", "sms"], "countries": ["7"], "dailyBudget": 300000,
          "targetPerHour": 5, "targetPerDay": 10, "ipPerHour": 10, "rangePerHour": 20, "captchaAfter": 2,
          "rangeDigits": 7, "smsCost": 400, "callCost": 100, "budgetAlertPercent": 80, "zone": "Europe/Moscow"
        }
        """;

    private readonly FakePlugin _keycloak = new();

    private static ProjectCodeSettings Settings { get; } = new(["call", "sms"], ["7", "375"], 300_000, 5, 10, 10, 20, 2);

    [Fact]
    public async Task Get_ByServiceAccountToken_SettingsAndServerSettings()
    {
        ProjectCodes codes = (await CreateCodes().GetAsync(default)).Value;

        Assert.Equal(["call", "sms"], codes.Settings.PhoneChannels);
        Assert.Equal(["7"], codes.Settings.Countries);
        Assert.Equal((300_000L, 5, 10, 10, 20, 2), (codes.Settings.DailyBudget, codes.Settings.TargetPerHour, codes.Settings.TargetPerDay,
            codes.Settings.IpPerHour, codes.Settings.RangePerHour, codes.Settings.CaptchaAfter));
        Assert.Equal(new ProjectCodeServerSettings(7, 400, 100, 80, "Europe/Moscow"), codes.Server);
        FakePlugin.Call get = _keycloak.Calls[1];
        Assert.Equal(("GET", $"{CODES}/settings", "service-account-token"), (get.Method, get.Url, get.Bearer));
    }

    [Fact]
    public async Task Set_PutsAllSettings()
    {
        Assert.True((await CreateCodes().SetAsync(Settings, default)).IsSuccess);

        FakePlugin.Call put = _keycloak.Calls[1];
        Assert.Equal(("PUT", $"{CODES}/settings", "service-account-token"), (put.Method, put.Url, put.Bearer));
        using JsonDocument body = JsonDocument.Parse(put.Body);
        Assert.Equal("375", body.RootElement.GetProperty("countries")[1].GetString());
        Assert.Equal(300_000, body.RootElement.GetProperty("dailyBudget").GetInt64());
        Assert.Equal(2, body.RootElement.GetProperty("captchaAfter").GetInt32());
        Assert.Equal("sms", body.RootElement.GetProperty("phoneChannels")[1].GetString());
    }

    [Fact]
    public async Task Usage_Read()
    {
        _keycloak.Body = """
            {
              "date": "2026-10-07", "zone": "Europe/Moscow",
              "budget": { "spent": 1500, "limit": 300000, "alertPercent": 80, "exhausted": false },
              "call": { "sent": 12, "cost": 1200 }, "sms": { "sent": 1, "cost": 400 }, "email": { "sent": 3, "cost": 0 },
              "sent": 16, "verified": 14, "verifiedPercent": 88
            }
            """;

        ProjectCodeUsage usage = (await CreateCodes().GetUsageAsync(default)).Value;

        Assert.Equal(new DateOnly(2026, 10, 7), usage.Date);
        Assert.Equal((1500L, 300_000L, false), (usage.Spent, usage.Budget, usage.Exhausted));
        Assert.Equal(new CodeChannelCount(12, 1200), usage.Call);
        Assert.Equal(new CodeChannelCount(3, 0), usage.Email);
        Assert.Equal((16L, 14L, (int?)88), (usage.Sent, usage.Verified, usage.VerifiedPercent));
        Assert.Equal($"{CODES}/usage", _keycloak.Calls[1].Url);
    }

    [Fact]
    public async Task Usage_NothingSent_PercentNull()
    {
        _keycloak.Body = """{ "date": "2026-10-07", "zone": "Europe/Moscow", "budget": { "spent": 0, "limit": 0, "alertPercent": 80, "exhausted": false }, "sent": 0, "verified": 0, "verifiedPercent": null }""";

        ProjectCodeUsage usage = (await CreateCodes().GetUsageAsync(default)).Value;

        Assert.Null(usage.VerifiedPercent);
        Assert.Equal(new CodeChannelCount(0, 0), usage.Sms);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "codes.not_permitted")]
    [InlineData(HttpStatusCode.Forbidden, "codes.not_permitted")]
    [InlineData(HttpStatusCode.NotFound, "codes.not_installed")]
    [InlineData(HttpStatusCode.InternalServerError, "auth.provider.unavailable")]
    public async Task Refused_ErrorByStatus(HttpStatusCode status, string code)
    {
        _keycloak.Status = status;

        Assert.Equal(code, (await CreateCodes().SetAsync(Settings, default)).Error.Messages[0].Code);
        Assert.Equal(code, (await CreateCodes().GetUsageAsync(default)).Error.Messages[0].Code);
    }

    [Fact]
    public async Task Set_BadRequest_PluginMessage()
    {
        _keycloak.Status = HttpStatusCode.BadRequest;
        _keycloak.Body = """{ "error": "Укажите хотя бы одну страну." }""";

        Error error = (await CreateCodes().SetAsync(Settings, default)).Error;

        Assert.Equal(("codes.invalid", "Укажите хотя бы одну страну."), (error.Messages[0].Code, error.Messages[0].Message));
    }

    [Fact]
    public async Task NotJson_Unavailable_NotException()
    {
        _keycloak.Body = "<html>proxy error</html>";

        Assert.Equal("auth.provider.unavailable", (await CreateCodes().GetAsync(default)).Error.Messages[0].Code);
    }

    [Fact]
    public async Task KeycloakUnreachable_Unavailable_NotException()
    {
        _keycloak.Unreachable = true;

        Assert.Equal("auth.provider.unavailable", (await CreateCodes().GetUsageAsync(default)).Error.Messages[0].Code);
    }

    public void Dispose() => _keycloak.Dispose();

    private IProjectCodes CreateCodes()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "http://localhost:8180/realms/shop",
            ["Authentication:MetadataAddress"] = "http://keycloak:8180/realms/shop/.well-known/openid-configuration",
            ["Authentication:Audience"] = "auth-api",
            ["Authentication:RequireHttpsMetadata"] = "false",
            ["Keycloak:ClientId"] = "auth-bff",
            ["Keycloak:ClientSecret"] = "test-secret",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<JwtAuthenticationOptions>().Bind(configuration.GetSection(JwtAuthenticationOptions.SECTION_NAME));
        services.AddInfrastructureKeycloak(configuration);
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => _keycloak));
        return services.BuildServiceProvider().GetRequiredService<IProjectCodes>();
    }

    /// <summary>Discovery, токен служебной учётки и плагин кодов. Discovery и ключи realm в журнал вызовов не попадают.</summary>
    private sealed class FakePlugin : HttpMessageHandler
    {
        private const string DISCOVERY = $$"""
            {
              "issuer": "http://localhost:8180/realms/shop",
              "token_endpoint": "{{REALM}}/protocol/openid-connect/token",
              "jwks_uri": "{{REALM}}/protocol/openid-connect/certs"
            }
            """;

        public List<Call> Calls { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string Body { get; set; } = SETTINGS_BODY;

        public bool Unreachable { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, DISCOVERY);
            }

            if (url.EndsWith("/certs", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "keys": [] }""");
            }

            if (Unreachable)
            {
                throw new HttpRequestException("connection refused");
            }

            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(new Call(request.Method.Method, url, request.Headers.Authorization?.Parameter, body));
            return url.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, """{ "access_token": "service-account-token", "expires_in": 300 }""")
                : Json(Status, Body);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public sealed record Call(string Method, string Url, string? Bearer, string Body);
    }
}
