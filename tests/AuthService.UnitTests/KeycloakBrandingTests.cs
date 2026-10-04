using System.Net;
using System.Text;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Infrastructure.Keycloak;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth;
using SharedKernel;
using Xunit;

namespace AuthService.UnitTests;

// Плагин Keycloak platform-branding подменён HTTP-заглушкой: какие запросы уходят и как разбираются ответы.
public sealed class KeycloakBrandingTests : IDisposable
{
    private const string REALM = "http://keycloak:8180/realms/shop";
    private const string BRANDING = $"{REALM}/platform-branding";

    private readonly FakePlugin _keycloak = new();

    [Fact]
    public async Task Get_WithoutToken_MissingLogoAndColor_AreNull()
    {
        _keycloak.Body = """{ "name": "Магазин", "theme": "dark" }""";

        ProjectBranding branding = (await CreateBranding().GetAsync(default)).Value;

        Assert.Equal(new ProjectBranding("Магазин", null, null, "dark"), branding);
        FakePlugin.Call call = Assert.Single(_keycloak.Calls);
        Assert.Equal(("GET", BRANDING, null), (call.Method, call.Url, call.Bearer));
    }

    [Fact]
    public async Task Set_ByServiceAccountToken_NullsSentExplicitly()
    {
        UnitResult<Error> result = await CreateBranding().SetAsync(new ProjectBranding("Магазин", null, "#1f6feb", "light"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal($"{REALM}/protocol/openid-connect/token", _keycloak.Calls[0].Url);
        FakePlugin.Call put = _keycloak.Calls[1];
        Assert.Equal(("PUT", BRANDING, "service-account-token"), (put.Method, put.Url, put.Bearer));
        using JsonDocument body = JsonDocument.Parse(put.Body);
        Assert.Equal("Магазин", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("logo").ValueKind);
        Assert.Equal("#1f6feb", body.RootElement.GetProperty("color").GetString());
        Assert.Equal("light", body.RootElement.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task GetApp_OwnValues_NullAsProject()
    {
        _keycloak.Body = """{ "name": "Менеджер 1С", "logo": null, "color": "#aa3300" }""";

        AppBranding branding = (await CreateBranding().GetAppAsync("manager-1c", default)).Value;

        Assert.Equal(new AppBranding("Менеджер 1С", null, "#aa3300"), branding);
        Assert.Equal(("GET", $"{BRANDING}/apps/manager-1c"), (_keycloak.Calls[0].Method, _keycloak.Calls[0].Url));
    }

    [Fact]
    public async Task SetApp_ByServiceAccountToken_NullsSentExplicitly()
    {
        Assert.True((await CreateBranding().SetAppAsync("manager-1c", new AppBranding(null, null, "#aa3300"), default)).IsSuccess);

        FakePlugin.Call put = _keycloak.Calls[1];
        Assert.Equal(("PUT", $"{BRANDING}/apps/manager-1c", "service-account-token"), (put.Method, put.Url, put.Bearer));
        using JsonDocument body = JsonDocument.Parse(put.Body);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("name").ValueKind);
        Assert.Equal("#aa3300", body.RootElement.GetProperty("color").GetString());
    }

    [Fact]
    public async Task App_NoClient_AppNotFound_NotNotInstalled()
    {
        _keycloak.Status = HttpStatusCode.NotFound;
        _keycloak.Body = """{ "error": "app_not_found" }""";

        Error error = (await CreateBranding().GetAppAsync("nope", default)).Error;

        Assert.Equal(IdentityProviderErrors.AppNotFound().Messages[0].Code, error.Messages[0].Code);
    }

    [Fact]
    public async Task Effective_WithApp_AsQuery()
    {
        _keycloak.Body = """{ "name": "Менеджер 1С", "color": "#aa3300", "theme": "light" }""";

        ProjectBranding branding = (await CreateBranding().GetEffectiveAsync("manager-1c", default)).Value;

        Assert.Equal(new ProjectBranding("Менеджер 1С", null, "#aa3300", "light"), branding);
        Assert.Equal($"{BRANDING}?app=manager-1c", _keycloak.Calls[0].Url);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "branding.not_permitted")]
    [InlineData(HttpStatusCode.Forbidden, "branding.not_permitted")]
    [InlineData(HttpStatusCode.NotFound, "branding.not_installed")]
    [InlineData(HttpStatusCode.InternalServerError, "auth.provider.unavailable")]
    public async Task Set_Refused_ErrorByStatus(HttpStatusCode status, string code)
    {
        _keycloak.Status = status;

        Error error = (await CreateBranding().SetAsync(new ProjectBranding("Магазин", null, null, "system"), default)).Error;

        Assert.Equal(code, error.Messages[0].Code);
    }

    [Fact]
    public async Task Set_BadRequest_PluginMessage()
    {
        _keycloak.Status = HttpStatusCode.BadRequest;
        _keycloak.Body = """{ "error": "Логотип — не больше 100 КБ." }""";

        Error error = (await CreateBranding().SetAsync(new ProjectBranding("Магазин", null, null, "system"), default)).Error;

        Assert.Equal(("branding.invalid", "Логотип — не больше 100 КБ."), (error.Messages[0].Code, error.Messages[0].Message));
    }

    [Fact]
    public async Task Get_NoPlugin_NotInstalled()
    {
        _keycloak.Status = HttpStatusCode.NotFound;

        Assert.Equal("branding.not_installed", (await CreateBranding().GetAsync(default)).Error.Messages[0].Code);
    }

    [Fact]
    public async Task Get_NotJson_Unavailable_NotException()
    {
        _keycloak.Body = "<html>proxy error</html>";

        Assert.Equal("auth.provider.unavailable", (await CreateBranding().GetAsync(default)).Error.Messages[0].Code);
    }

    [Fact]
    public async Task KeycloakUnreachable_Unavailable_NotException()
    {
        _keycloak.Unreachable = true;

        Assert.Equal("auth.provider.unavailable", (await CreateBranding().GetAsync(default)).Error.Messages[0].Code);
    }

    public void Dispose() => _keycloak.Dispose();

    private IProjectBranding CreateBranding()
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
        return services.BuildServiceProvider().GetRequiredService<IProjectBranding>();
    }

    /// <summary>Discovery, токен служебной учётки и плагин оформления. Discovery и ключи realm в журнал вызовов не попадают.</summary>
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

        public string Body { get; set; } = """{ "name": "Магазин", "theme": "system" }""";

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
