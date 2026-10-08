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

// Client registration API Keycloak подменён HTTP-заглушкой: какие запросы уходят с какими токенами и как разбираются ответы.
public sealed class KeycloakServiceRegistryTests : IDisposable
{
    private const string REALM = "http://keycloak:8180/realms/shop";
    private const string REGISTRATIONS = $"{REALM}/clients-registrations/default";
    private const string CLIENT = "service-orders";
    private static readonly Guid _serviceAccountId = Guid.Parse("5f0c1a52-8f9e-4a43-9d0c-2b6a1f7e9a11");

    private readonly FakeRegistrations _keycloak = new();

    [Fact]
    public async Task Register_ByRegistrationToken_OnlyServiceAccount_AndServiceAccountFromFirstToken()
    {
        RegisteredService service = (await CreateRegistry().RegisterAsync(CLIENT, "Заказы", default)).Value;

        FakeRegistrations.Call register = _keycloak.Calls[0];
        Assert.Equal(("POST", REGISTRATIONS, "initial-access-token"), (register.Method, register.Url, register.Bearer));
        using JsonDocument body = JsonDocument.Parse(register.Body);
        Assert.Equal(CLIENT, body.RootElement.GetProperty("clientId").GetString());
        Assert.True(body.RootElement.GetProperty("serviceAccountsEnabled").GetBoolean());
        Assert.False(body.RootElement.GetProperty("publicClient").GetBoolean());
        Assert.False(body.RootElement.GetProperty("standardFlowEnabled").GetBoolean());
        Assert.False(body.RootElement.GetProperty("directAccessGrantsEnabled").GetBoolean());
        Assert.False(body.RootElement.GetProperty("implicitFlowEnabled").GetBoolean());

        FakeRegistrations.Call token = _keycloak.Calls[1];
        Assert.Equal($"{REALM}/protocol/openid-connect/token", token.Url);
        Assert.Contains("grant_type=client_credentials", token.Body, StringComparison.Ordinal);
        Assert.Contains("client_secret=secret-1", token.Body, StringComparison.Ordinal);

        Assert.Equal("secret-1", service.ClientSecret);
        Assert.Equal("rat-1", service.RegistrationToken);
        Assert.Equal(_serviceAccountId, service.ServiceAccountId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "services.registration_rejected")]
    [InlineData(HttpStatusCode.Conflict, "services.exists")]
    [InlineData(HttpStatusCode.BadRequest, "services.registration_invalid")]
    [InlineData(HttpStatusCode.InternalServerError, "auth.provider.unavailable")]
    public async Task Register_Rejected_ErrorByStatus_NothingElseCalled(HttpStatusCode status, string code)
    {
        _keycloak.RegisterStatus = status;

        Error error = (await CreateRegistry().RegisterAsync(CLIENT, "Заказы", default)).Error;

        Assert.Equal(code, error.Messages[0].Code);
        Assert.Single(_keycloak.Calls);
    }

    [Fact]
    public async Task Register_ClientIdInUse_Exists()
    {
        // Так Keycloak отвечает на занятый clientId (проверено на настоящем Keycloak — AuthService.InfrastructureTests).
        _keycloak.RegisterStatus = HttpStatusCode.BadRequest;
        _keycloak.RegisterBody = """{"error":"invalid_client_metadata","error_description":"Client Identifier in use"}""";

        Error error = (await CreateRegistry().RegisterAsync(CLIENT, "Заказы", default)).Error;

        Assert.Equal("services.exists", error.Messages[0].Code);
    }

    [Fact]
    public async Task Register_NoServiceAccountToken_ClientRemovedByItsManagementToken()
    {
        _keycloak.TokenStatus = HttpStatusCode.Unauthorized;

        Error error = (await CreateRegistry().RegisterAsync(CLIENT, "Заказы", default)).Error;

        Assert.Equal("auth.provider.unavailable", error.Messages[0].Code);
        FakeRegistrations.Call delete = _keycloak.Calls[^1];
        Assert.Equal(("DELETE", $"{REGISTRATIONS}/{CLIENT}", "rat-1"), (delete.Method, delete.Url, delete.Bearer));
    }

    [Fact]
    public async Task Register_NotJson_Unavailable_NotException()
    {
        _keycloak.RegisterBody = "<html>proxy error</html>";

        Error error = (await CreateRegistry().RegisterAsync(CLIENT, "Заказы", default)).Error;

        Assert.Equal("auth.provider.unavailable", error.Messages[0].Code);
    }

    [Fact]
    public async Task Rotate_ReadsWithStoredToken_UpdatesWithFreshToken_NewSecretAndToken_NoTokenRequest()
    {
        RotatedSecret rotated = (await CreateRegistry().RotateSecretAsync(CLIENT, "rat-1", default)).Value;

        Assert.Equal(2, _keycloak.Calls.Count);
        FakeRegistrations.Call read = _keycloak.Calls[0];
        Assert.Equal(("GET", $"{REGISTRATIONS}/{CLIENT}", "rat-1"), (read.Method, read.Url, read.Bearer));
        FakeRegistrations.Call update = _keycloak.Calls[1];
        Assert.Equal(("PUT", $"{REGISTRATIONS}/{CLIENT}", "rat-2"), (update.Method, update.Url, update.Bearer));
        using JsonDocument body = JsonDocument.Parse(update.Body);
        string secret = body.RootElement.GetProperty("secret").GetString()!;
        Assert.NotEqual("secret-1", secret);
        Assert.True(secret.Length >= 40, secret);

        Assert.Equal(secret, rotated.ClientSecret);
        Assert.Equal("rat-3", rotated.RegistrationToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "services.management_rejected")]
    [InlineData(HttpStatusCode.Forbidden, "services.management_rejected")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "auth.provider.unavailable")]
    public async Task Rotate_ReadRejected_ErrorByStatus_NoUpdate(HttpStatusCode status, string code)
    {
        _keycloak.ReadStatus = status;

        Error error = (await CreateRegistry().RotateSecretAsync(CLIENT, "rat-1", default)).Error;

        Assert.Equal(code, error.Messages[0].Code);
        Assert.Single(_keycloak.Calls);
    }

    [Fact]
    public async Task Rotate_UpdateAnswerNotJson_Unavailable_NotException()
    {
        _keycloak.UpdateBody = "not json";

        Result<RotatedSecret, Error> result = await CreateRegistry().RotateSecretAsync(CLIENT, "rat-1", default);

        Assert.Equal("auth.provider.unavailable", result.Error.Messages[0].Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, null)]
    [InlineData(HttpStatusCode.NotFound, null)]
    [InlineData(HttpStatusCode.Unauthorized, "services.management_rejected")]
    [InlineData(HttpStatusCode.BadGateway, "auth.provider.unavailable")]
    public async Task Delete_GoneIsSuccess_RejectedIsError(HttpStatusCode status, string? code)
    {
        _keycloak.DeleteStatus = status;

        UnitResult<Error> result = await CreateRegistry().DeleteAsync(CLIENT, "rat-1", default);

        Assert.Equal(code, result.IsFailure ? result.Error.Messages[0].Code : null);
        FakeRegistrations.Call delete = Assert.Single(_keycloak.Calls);
        Assert.Equal(("DELETE", $"{REGISTRATIONS}/{CLIENT}", "rat-1"), (delete.Method, delete.Url, delete.Bearer));
    }

    [Fact]
    public async Task KeycloakUnreachable_Unavailable_NotException()
    {
        _keycloak.Unreachable = true;

        Error error = (await CreateRegistry().RegisterAsync(CLIENT, "Заказы", default)).Error;

        Assert.Equal("auth.provider.unavailable", error.Messages[0].Code);
    }

    public void Dispose() => _keycloak.Dispose();

    private IServiceRegistry CreateRegistry()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "http://localhost:8180/realms/shop",
            ["Authentication:MetadataAddress"] = "http://keycloak:8180/realms/shop/.well-known/openid-configuration",
            ["Authentication:Audience"] = "auth-api",
            ["Authentication:RequireHttpsMetadata"] = "false",
            ["Keycloak:ClientId"] = "auth-bff",
            ["Keycloak:ClientSecret"] = "test-secret",
            ["Keycloak:RegistrationToken"] = "initial-access-token",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<JwtAuthenticationOptions>().Bind(configuration.GetSection(JwtAuthenticationOptions.SECTION_NAME));
        services.AddInfrastructureKeycloak(configuration);
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => _keycloak));
        return services.BuildServiceProvider().GetRequiredService<IServiceRegistry>();
    }

    /// <summary>
    /// Discovery, регистрация (токен управления rat-1), чтение (выдаёт rat-2), обновление (выдаёт rat-3), удаление и
    /// токен служебной учётки нового клиента. Discovery и ключи realm в журнал вызовов не попадают.
    /// </summary>
    private sealed class FakeRegistrations : HttpMessageHandler
    {
        private const string DISCOVERY = $$"""
            {
              "issuer": "http://localhost:8180/realms/shop",
              "token_endpoint": "{{REALM}}/protocol/openid-connect/token",
              "jwks_uri": "{{REALM}}/protocol/openid-connect/certs"
            }
            """;

        public List<Call> Calls { get; } = [];

        public HttpStatusCode RegisterStatus { get; set; } = HttpStatusCode.Created;

        public string? RegisterBody { get; set; }

        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;

        public HttpStatusCode ReadStatus { get; set; } = HttpStatusCode.OK;

        public string? UpdateBody { get; set; }

        public HttpStatusCode DeleteStatus { get; set; } = HttpStatusCode.NoContent;

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

            if (url.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                return Json(TokenStatus, $$"""{ "access_token": "{{ServiceAccountToken()}}" }""");
            }

            return request.Method.Method switch
            {
                "POST" => Json(RegisterStatus, RegisterBody ?? Client("secret-1", "rat-1")),
                "GET" => Json(ReadStatus, Client("secret-1", "rat-2")),
                "PUT" => Json(HttpStatusCode.OK, UpdateBody ?? Client(SecretOf(body), "rat-3")),
                "DELETE" => new HttpResponseMessage(DeleteStatus),
                _ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed),
            };
        }

        private static string Client(string secret, string registrationToken) => $$"""
            {
              "clientId": "{{CLIENT}}", "name": "Заказы", "publicClient": false, "serviceAccountsEnabled": true,
              "standardFlowEnabled": false, "directAccessGrantsEnabled": false, "implicitFlowEnabled": false,
              "secret": "{{secret}}", "registrationAccessToken": "{{registrationToken}}"
            }
            """;

        private static string SecretOf(string body)
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("secret").GetString()!;
        }

        // Неподписанный JWT: сервису входа нужен только sub.
        private static string ServiceAccountToken() =>
            $"{Base64Url("""{"alg":"none","typ":"JWT"}""")}.{Base64Url($$"""{"sub":"{{_serviceAccountId}}"}""")}.";

        private static string Base64Url(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public sealed record Call(string Method, string Url, string? Bearer, string Body);
    }
}
