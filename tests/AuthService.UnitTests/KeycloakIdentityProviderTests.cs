using System.Net;
using System.Security.Cryptography;
using System.Text;
using AuthService.Core.Abstractions;
using AuthService.Infrastructure.Keycloak;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using PlatformAuth;
using SharedKernel;
using Xunit;

namespace AuthService.UnitTests;

// Keycloak подменён HTTP-заглушкой: проверяем, что клиент отправляет и как разбирает ответы.
public sealed class KeycloakIdentityProviderTests : IDisposable
{
    private const string EXTERNAL = "http://localhost:8180/realms/sandbox/protocol/openid-connect";
    private const string INTERNAL = "http://keycloak:8180/realms/sandbox/protocol/openid-connect";
    private static readonly Uri _callback = new("http://localhost:8080/auth/callback");
    private static readonly DateTimeOffset _now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeKeycloak _keycloak = new();

    [Fact]
    public async Task AuthorizationRequest_PointsBrowserToExternalLoginPageWithPkce()
    {
        AuthorizationRequest request = (await CreateProvider().CreateAuthorizationRequestAsync(_callback, forceLogin: false, action: null, app: null, default)).Value;

        Assert.StartsWith($"{EXTERNAL}/auth?", request.Url.ToString(), StringComparison.Ordinal);
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(request.Url.Query);
        Assert.Equal("sandbox-bff", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("query", query["response_mode"]);
        Assert.Equal(_callback.ToString(), query["redirect_uri"]);
        Assert.Equal(request.State, query["state"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(Challenge(request.CodeVerifier), query["code_challenge"]);
        Assert.False(query.ContainsKey("code_verifier"));
        Assert.False(query.ContainsKey("prompt"));
        Assert.False(query.ContainsKey("kc_action"));
    }

    [Fact]
    public async Task ForceLogin_AsksKeycloakToLoginAgain()
    {
        AuthorizationRequest request = (await CreateProvider().CreateAuthorizationRequestAsync(_callback, forceLogin: true, action: null, app: null, default)).Value;

        Assert.Equal("login", QueryHelpers.ParseQuery(request.Url.Query)["prompt"]);
    }

    [Theory]
    [InlineData(AccountAction.UpdatePassword, null, "UPDATE_PASSWORD")]
    [InlineData(AccountAction.UpdateEmail, null, "UPDATE_EMAIL")]
    [InlineData(AccountAction.ConfigureTotp, null, "CONFIGURE_TOTP")]
    [InlineData(AccountAction.RegisterPasskey, null, "webauthn-register-passwordless")]
    [InlineData(AccountAction.ConfigureRecoveryCodes, null, "CONFIGURE_RECOVERY_AUTHN_CODES")]
    [InlineData(AccountAction.DeleteCredential, "4b4f2c3e-8f1a-4d7e-9a51-0d6a1c2b3e4f", "delete_credential:4b4f2c3e-8f1a-4d7e-9a51-0d6a1c2b3e4f")]
    public async Task AccountAction_AsksKeycloakForRequiredAction(AccountAction action, string? credential, string kcAction)
    {
        AuthorizationRequest request = (await CreateProvider()
            .CreateAuthorizationRequestAsync(_callback, forceLogin: false, new AccountActionRequest(action, credential), app: null, default)).Value;

        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(request.Url.Query);
        Assert.Equal(kcAction, query["kc_action"]);
        Assert.False(query.ContainsKey("prompt"));
    }

    [Fact]
    public async Task AuthorizationRequest_StateAndVerifierAreFreshEachTime()
    {
        IIdentityProvider provider = CreateProvider();

        AuthorizationRequest first = (await provider.CreateAuthorizationRequestAsync(_callback, forceLogin: false, action: null, app: null, default)).Value;
        AuthorizationRequest second = (await provider.CreateAuthorizationRequestAsync(_callback, forceLogin: false, action: null, app: null, default)).Value;

        Assert.NotEqual(first.State, second.State);
        Assert.NotEqual(first.CodeVerifier, second.CodeVerifier);
        Assert.True(first.CodeVerifier.Length >= 43);
    }

    [Fact]
    public async Task ExchangeCode_PostsToInternalTokenEndpoint_AndReturnsTokens()
    {
        _keycloak.TokenResponse = TokensJson;

        TokenSet tokens = (await CreateProvider().ExchangeCodeAsync("the-code", "the-verifier", _callback, default)).Value;

        FakeKeycloak.Request request = Assert.Single(_keycloak.Posts);
        Assert.Equal($"{INTERNAL}/token", request.Url);
        Assert.Equal("authorization_code", request.Form["grant_type"]);
        Assert.Equal("the-code", request.Form["code"]);
        Assert.Equal("the-verifier", request.Form["code_verifier"]);
        Assert.Equal(_callback.ToString(), request.Form["redirect_uri"]);
        Assert.Equal("sandbox-bff", request.Form["client_id"]);
        Assert.Equal("test-secret", request.Form["client_secret"]);

        Assert.Equal("access-1", tokens.AccessToken);
        Assert.Equal(_now.AddSeconds(900), tokens.AccessTokenExpiresAt);
        Assert.Equal("refresh-1", tokens.RefreshToken);
        Assert.Equal(_now.AddSeconds(1800), tokens.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task Refresh_SendsRefreshToken()
    {
        _keycloak.TokenResponse = TokensJson;

        Result<TokenSet, Error> result = await CreateProvider().RefreshAsync("refresh-0", default);

        Assert.True(result.IsSuccess);
        FakeKeycloak.Request request = Assert.Single(_keycloak.Posts);
        Assert.Equal("refresh_token", request.Form["grant_type"]);
        Assert.Equal("refresh-0", request.Form["refresh_token"]);
    }

    [Fact]
    public async Task Refresh_InvalidGrant_IsSessionInvalid()
    {
        _keycloak.TokenResponse = (HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token is not active"}""");

        Error error = (await CreateProvider().RefreshAsync("stale", default)).Error;

        Assert.Equal(ErrorType.AUTHENTICATION, error.Type);
        Assert.Equal("auth.session.invalid", error.Messages[0].Code);
    }

    [Fact]
    public async Task ServerError_IsTransientUnavailable()
    {
        _keycloak.TokenResponse = (HttpStatusCode.InternalServerError, "{}");

        Error error = (await CreateProvider().RefreshAsync("any", default)).Error;

        Assert.Equal("auth.provider.unavailable", error.Messages[0].Code);
        Assert.Equal(ErrorBehavior.Transient, error.Behavior);
    }

    [Fact]
    public async Task NetworkFailure_IsUnavailable()
    {
        _keycloak.FailPosts = true;

        Error error = (await CreateProvider().ExchangeCodeAsync("c", "v", _callback, default)).Error;

        Assert.Equal("auth.provider.unavailable", error.Messages[0].Code);
    }

    [Fact]
    public async Task Logout_PostsRefreshTokenToInternalEndpoint_EvenThoughDiscoveryGivesExternal()
    {
        _keycloak.LogoutResponse = (HttpStatusCode.NoContent, string.Empty);

        UnitResult<Error> result = await CreateProvider().LogoutAsync("refresh-1", default);

        Assert.True(result.IsSuccess);
        FakeKeycloak.Request request = Assert.Single(_keycloak.Posts);
        Assert.Equal($"{INTERNAL}/logout", request.Url);
        Assert.Equal("refresh-1", request.Form["refresh_token"]);
        Assert.Equal("test-secret", request.Form["client_secret"]);
    }

    [Fact]
    public void MissingClientSecret_FailsOnStart()
    {
        ServiceProvider services = BuildServices(clientSecret: null);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<KeycloakOptions>>().Value);
        Assert.Contains("Keycloak:ClientSecret is required", exception.Message, StringComparison.Ordinal);
    }

    // Смена секрета BFF (docs/secrets.md): сервис уже с новым секретом, Keycloak ещё со старым — запрос повторяется со старым.
    [Fact]
    public async Task SecretRotation_KeycloakStillHasPrevious_RetriesWithPrevious()
    {
        _keycloak.TokenResponse = TokensJson;
        _keycloak.AcceptedSecret = "old-secret";

        Result<TokenSet, Error> result = await CreateProvider("new-secret", previousSecret: "old-secret").RefreshAsync("refresh-0", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(["new-secret", "old-secret"], _keycloak.Posts.Select(post => post.Form["client_secret"].ToString()));
    }

    [Fact]
    public async Task SecretRotation_KeycloakHasNew_NoRetry()
    {
        _keycloak.TokenResponse = TokensJson;
        _keycloak.AcceptedSecret = "new-secret";

        Result<TokenSet, Error> result = await CreateProvider("new-secret", previousSecret: "old-secret").RefreshAsync("refresh-0", default);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-secret", Assert.Single(_keycloak.Posts).Form["client_secret"]);
    }

    [Fact]
    public async Task SecretRejected_NoPrevious_IsUnavailableWithoutRetry()
    {
        _keycloak.TokenResponse = TokensJson;
        _keycloak.AcceptedSecret = "other-secret";

        Error error = (await CreateProvider().RefreshAsync("refresh-0", default)).Error;

        Assert.Equal("auth.provider.unavailable", error.Messages[0].Code);
        Assert.Single(_keycloak.Posts);
    }

    [Fact]
    public async Task InvalidGrant_WithPrevious_NotRetried()
    {
        _keycloak.TokenResponse = (HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token is not active"}""");

        Error error = (await CreateProvider("new-secret", previousSecret: "old-secret").RefreshAsync("stale", default)).Error;

        Assert.Equal("auth.session.invalid", error.Messages[0].Code);
        Assert.Single(_keycloak.Posts);
    }

    public void Dispose() => _keycloak.Dispose();

    private static (HttpStatusCode, string) TokensJson =>
        (HttpStatusCode.OK, """{"access_token":"access-1","expires_in":900,"refresh_token":"refresh-1","refresh_expires_in":1800,"token_type":"Bearer"}""");

    private IIdentityProvider CreateProvider(string clientSecret = "test-secret", string? previousSecret = null) =>
        BuildServices(clientSecret, previousSecret).GetRequiredService<IIdentityProvider>();

    private ServiceProvider BuildServices(string? clientSecret, string? previousSecret = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "http://localhost:8180/realms/sandbox",
            ["Authentication:MetadataAddress"] = "http://keycloak:8180/realms/sandbox/.well-known/openid-configuration",
            ["Authentication:Audience"] = "sandbox-api",
            ["Authentication:RequireHttpsMetadata"] = "false",
            ["Keycloak:ClientId"] = "sandbox-bff",
            ["Keycloak:ClientSecret"] = clientSecret,
            ["Keycloak:PreviousClientSecret"] = previousSecret,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<JwtAuthenticationOptions>().Bind(configuration.GetSection(JwtAuthenticationOptions.SECTION_NAME));
        services.AddInfrastructureKeycloak(configuration);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(_now));
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => _keycloak));
        return services.BuildServiceProvider();
    }

    private static string Challenge(string verifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Discovery, JWKS, token и logout Keycloak — как их видит контейнер: token и certs внутренние,
    /// authorization и end_session — внешние (так отвечает настоящий Keycloak с KC_HOSTNAME_BACKCHANNEL_DYNAMIC).
    /// </summary>
    private sealed class FakeKeycloak : HttpMessageHandler
    {
        private const string DISCOVERY = $$"""
            {
              "issuer": "http://localhost:8180/realms/sandbox",
              "authorization_endpoint": "{{EXTERNAL}}/auth",
              "token_endpoint": "{{INTERNAL}}/token",
              "end_session_endpoint": "{{EXTERNAL}}/logout",
              "jwks_uri": "{{INTERNAL}}/certs"
            }
            """;

        public List<Request> Posts { get; } = [];

        public (HttpStatusCode Status, string Body) TokenResponse { get; set; } = (HttpStatusCode.OK, "{}");

        public (HttpStatusCode Status, string Body) LogoutResponse { get; set; } = (HttpStatusCode.NoContent, string.Empty);

        public bool FailPosts { get; set; }

        /// <summary>Секрет клиента, который принимает Keycloak; null — любой.</summary>
        public string? AcceptedSecret { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, url.EndsWith("/certs", StringComparison.Ordinal) ? """{"keys":[]}""" : DISCOVERY);
            }

            if (FailPosts)
            {
                throw new HttpRequestException("connection refused");
            }

            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Dictionary<string, StringValues> form = QueryHelpers.ParseQuery(body);
            Posts.Add(new Request(url, form));
            if (AcceptedSecret is not null && !string.Equals(form["client_secret"], AcceptedSecret, StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.Unauthorized, """{"error":"unauthorized_client","error_description":"Invalid client or Invalid client credentials"}""");
            }

            (HttpStatusCode status, string json) = url.EndsWith("/logout", StringComparison.Ordinal) ? LogoutResponse : TokenResponse;
            return Json(status, json);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        public sealed record Request(string Url, Dictionary<string, StringValues> Form);
    }
}
