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

// Плагин Keycloak platform-sign-in подменён HTTP-заглушкой: какие запросы уходят и как разбираются ответы.
public sealed class KeycloakSignInTests : IDisposable
{
    private const string REALM = "http://keycloak:8180/realms/shop";
    private const string METHODS = $"{REALM}/platform-sign-in/methods";

    private const string METHODS_BODY = """
        {
          "methods": {
            "phone": { "status": "transition", "until": "2026-11-05", "since": "2026-10-07T09:00:00Z" },
            "email": { "status": "on" }, "password": { "status": "off" }, "passkey": { "status": "on" },
            "yandex": { "status": "on", "configured": true }, "esia": { "status": "off", "configured": false }
          },
          "twoFactor": "required"
        }
        """;

    private readonly FakePlugin _keycloak = new();

    [Fact]
    public async Task Get_ByServiceAccountToken()
    {
        ProjectSignInSettings settings = (await CreateSignIn().GetAsync(default)).Value;

        Assert.True(settings.TwoFactorRequired);
        Assert.Equal(new ProjectSignInMethod("transition", new DateOnly(2026, 11, 5), new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero)), settings.Methods["phone"]);
        Assert.Equal(new ProjectSignInMethod("off", Configured: false), settings.Methods["esia"]);
        FakePlugin.Call get = _keycloak.Calls[1];
        Assert.Equal(("GET", METHODS, "service-account-token"), (get.Method, get.Url, get.Bearer));
    }

    [Fact]
    public async Task Set_PutsAllMethods()
    {
        var settings = new ProjectSignInSettings(
            ProjectSignInRules.Methods.ToDictionary(method => method, _ => new ProjectSignInMethod("on"), StringComparer.Ordinal),
            TwoFactorRequired: false);
        var methods = new Dictionary<string, ProjectSignInMethod>(settings.Methods, StringComparer.Ordinal)
        {
            ["password"] = new("transition", new DateOnly(2026, 11, 5)),
        };

        Assert.True((await CreateSignIn().SetAsync(settings with { Methods = methods }, default)).IsSuccess);

        FakePlugin.Call put = _keycloak.Calls[1];
        Assert.Equal(("PUT", METHODS), (put.Method, put.Url));
        using JsonDocument body = JsonDocument.Parse(put.Body);
        Assert.Equal("2026-11-05", body.RootElement.GetProperty("methods").GetProperty("password").GetProperty("until").GetString());
        Assert.Equal("optional", body.RootElement.GetProperty("twoFactor").GetString());
    }

    [Fact]
    public async Task Affected_Read()
    {
        _keycloak.Body = """{ "method": "phone", "users": 8410, "only": 1240 }""";

        SignInMethodUsers users = (await CreateSignIn().GetAffectedAsync("phone", default)).Value;

        Assert.Equal(new SignInMethodUsers("phone", 8410, 1240), users);
        Assert.Equal($"{METHODS}/phone/affected", _keycloak.Calls[1].Url);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "sign_in.not_permitted")]
    [InlineData(HttpStatusCode.NotFound, "sign_in.not_installed")]
    [InlineData(HttpStatusCode.BadGateway, "auth.provider.unavailable")]
    public async Task Refused_ErrorByStatus(HttpStatusCode status, string code)
    {
        _keycloak.Status = status;

        Assert.Equal(code, (await CreateSignIn().GetAsync(default)).Error.Messages[0].Code);
    }

    [Fact]
    public async Task BadRequest_PluginMessage()
    {
        _keycloak.Status = HttpStatusCode.BadRequest;
        _keycloak.Body = """{ "error": "Яндекс ID не настроен в Keycloak." }""";

        Error error = (await CreateSignIn().GetAsync(default)).Error;

        Assert.Equal(("sign_in.invalid", "Яндекс ID не настроен в Keycloak."), (error.Messages[0].Code, error.Messages[0].Message));
    }

    public void Dispose() => _keycloak.Dispose();

    private IProjectSignIn CreateSignIn()
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
        return services.BuildServiceProvider().GetRequiredService<IProjectSignIn>();
    }

    /// <summary>Discovery, токен служебной учётки и плагин способов входа. Discovery и ключи realm в журнал вызовов не попадают.</summary>
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

        public string Body { get; set; } = METHODS_BODY;

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
