using System.Net;
using System.Text;
using AuthService.Core.Abstractions;
using AuthService.Infrastructure.Keycloak;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using PlatformAuth;
using SharedKernel;
using Xunit;

namespace AuthService.UnitTests;

// Admin API Keycloak подменён HTTP-заглушкой: что клиент отправляет и как разбирает ответы.
public sealed class KeycloakAdminClientTests : IDisposable
{
    private const string INTERNAL = "http://keycloak:8180/realms/sandbox/protocol/openid-connect";
    private const string ADMIN = "http://keycloak:8180/admin/realms/sandbox";
    private static readonly Guid _userId = Guid.Parse("a2b3805f-2d09-4c35-ab2e-adc8ae75b01c");

    private readonly FakeKeycloak _keycloak = new();

    [Fact]
    public async Task GetUser_UsesServiceAccountToken_AndInternalAdminApiNextToTokenEndpoint()
    {
        IdentityUser user = (await CreateClient().GetUserAsync(_userId, default)).Value;

        FakeKeycloak.Request token = Assert.Single(_keycloak.TokenRequests);
        Assert.Equal($"{INTERNAL}/token", token.Url);
        Assert.Equal("client_credentials", token.Form["grant_type"]);
        Assert.Equal("sandbox-bff", token.Form["client_id"]);
        Assert.Equal("test-secret", token.Form["client_secret"]);

        (string url, string? bearer) = Assert.Single(_keycloak.AdminRequests);
        Assert.Equal($"{ADMIN}/users/{_userId}", url);
        Assert.Equal("service-token", bearer);

        Assert.Equal(_userId, user.Id);
        Assert.Equal("79990000001", user.Username);
        Assert.Equal("customer@sandbox.test", user.Email);
        Assert.Equal("+79990000001", user.Phone);
        Assert.Equal("customer", user.ActiveMode);
        Assert.True(user.Enabled);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1790000000000), user.CreatedAt);
    }

    [Fact]
    public async Task ServiceAccountToken_IsReusedUntilExpiry()
    {
        IIdentityAdmin client = CreateClient();

        await client.GetUserAsync(_userId, default);
        await client.GetUserAsync(_userId, default);

        Assert.Single(_keycloak.TokenRequests);
        Assert.Equal(2, _keycloak.AdminRequests.Count);
    }

    [Fact]
    public async Task UnknownUser_IsNotFound()
    {
        _keycloak.UserStatus = HttpStatusCode.NotFound;

        Error error = (await CreateClient().GetUserAsync(_userId, default)).Error;

        Assert.Equal(ErrorType.NOT_FOUND, error.Type);
        Assert.Equal("users.not_found", error.Messages[0].Code);
    }

    [Fact]
    public async Task RejectedServiceAccount_IsUnavailable()
    {
        _keycloak.TokenStatus = HttpStatusCode.Unauthorized;

        Result<IdentityUser, Error> result = await CreateClient().GetUserAsync(_userId, default);

        Assert.Equal("auth.provider.unavailable", result.Error.Messages[0].Code);
        Assert.Empty(_keycloak.AdminRequests);
    }

    public void Dispose() => _keycloak.Dispose();

    private IIdentityAdmin CreateClient()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "http://localhost:8180/realms/sandbox",
            ["Authentication:MetadataAddress"] = "http://keycloak:8180/realms/sandbox/.well-known/openid-configuration",
            ["Authentication:Audience"] = "sandbox-api",
            ["Authentication:RequireHttpsMetadata"] = "false",
            ["Keycloak:ClientId"] = "sandbox-bff",
            ["Keycloak:ClientSecret"] = "test-secret",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<JwtAuthenticationOptions>().Bind(configuration.GetSection(JwtAuthenticationOptions.SECTION_NAME));
        services.AddInfrastructureKeycloak(configuration);
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => _keycloak));
        return services.BuildServiceProvider().GetRequiredService<IIdentityAdmin>();
    }

    /// <summary>Discovery (token — внутренний), token по client_credentials и GET пользователя в Admin API.</summary>
    private sealed class FakeKeycloak : HttpMessageHandler
    {
        private const string DISCOVERY = $$"""
            {
              "issuer": "http://localhost:8180/realms/sandbox",
              "authorization_endpoint": "http://localhost:8180/realms/sandbox/protocol/openid-connect/auth",
              "token_endpoint": "{{INTERNAL}}/token",
              "jwks_uri": "{{INTERNAL}}/certs"
            }
            """;

        private const string USER = """
            {
              "id": "a2b3805f-2d09-4c35-ab2e-adc8ae75b01c",
              "username": "79990000001",
              "email": "customer@sandbox.test",
              "enabled": true,
              "createdTimestamp": 1790000000000,
              "attributes": { "phone": ["+79990000001"], "active_mode": ["customer"] }
            }
            """;

        public List<Request> TokenRequests { get; } = [];

        public List<(string Url, string? Bearer)> AdminRequests { get; } = [];

        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;

        public HttpStatusCode UserStatus { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/admin/realms/", StringComparison.Ordinal))
            {
                AdminRequests.Add((url, request.Headers.Authorization?.Parameter));
                return Json(UserStatus, UserStatus == HttpStatusCode.OK ? USER : """{"error":"User not found"}""");
            }

            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, url.EndsWith("/certs", StringComparison.Ordinal) ? """{"keys":[]}""" : DISCOVERY);
            }

            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            TokenRequests.Add(new Request(url, QueryHelpers.ParseQuery(body)));
            return Json(TokenStatus, TokenStatus == HttpStatusCode.OK
                ? """{"access_token":"service-token","expires_in":300,"token_type":"Bearer"}"""
                : """{"error":"unauthorized_client"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        public sealed record Request(string Url, Dictionary<string, StringValues> Form);
    }
}
