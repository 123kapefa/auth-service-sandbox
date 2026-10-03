using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth.ServiceTokens;
using Xunit;

namespace Shared.UnitTests;

// Ключ сервиса → машинный токен: кэш, повтор при 401 (ключ перевыпущен, права сужены), отозванный ключ.
public sealed class ServiceTokensTests
{
    private const string TOKEN_URL = "http://keycloak:8180/realms/shop/protocol/openid-connect/token";

    [Fact]
    public async Task Token_IsCached_UntilExpiry()
    {
        StubHandler keycloak = new(_ => Token("t1"));
        using ServiceProvider services = Services(keycloak);
        IServiceTokenProvider tokens = services.GetRequiredService<IServiceTokenProvider>();

        Assert.Equal("t1", await tokens.GetTokenAsync(default));
        Assert.Equal("t1", await tokens.GetTokenAsync(default));

        HttpRequestMessage request = Assert.Single(keycloak.Requests);
        Assert.Equal(TOKEN_URL, request.RequestUri!.ToString());
        Assert.Contains("grant_type=client_credentials", keycloak.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("client_id=service-orders", keycloak.Bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthorized_RetriesOnce_WithNewToken()
    {
        int issued = 0;
        StubHandler keycloak = new(_ => Token($"t{++issued}"));
        StubHandler api = new(request => request.Headers.Authorization!.Parameter == "t1"
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : new HttpResponseMessage(HttpStatusCode.OK));
        using ServiceProvider services = Services(keycloak);
        using HttpClient client = Client(services, api);

        HttpResponseMessage response = await client.GetAsync(new Uri("http://notifications/send"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["t1", "t2"], api.Tokens);
    }

    [Fact]
    public async Task UnauthorizedTwice_ReturnsUnauthorized_NoLoop()
    {
        StubHandler keycloak = new(_ => Token("t"));
        StubHandler api = new(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using ServiceProvider services = Services(keycloak);
        using HttpClient client = Client(services, api);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("http://notifications/send"))).StatusCode);
        Assert.Equal(2, api.Requests.Count);
    }

    [Fact]
    public async Task RevokedKey_Throws()
    {
        StubHandler keycloak = new(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":"invalid_client"}""", Encoding.UTF8, "application/json"),
        });
        using ServiceProvider services = Services(keycloak);

        ServiceTokenException error = await Assert.ThrowsAsync<ServiceTokenException>(
            () => services.GetRequiredService<IServiceTokenProvider>().GetTokenAsync(default));
        Assert.Contains("service-orders", error.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Services(StubHandler keycloak)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceToken:Authority"] = "http://localhost:8180/realms/shop",
            ["ServiceToken:TokenEndpoint"] = TOKEN_URL,
            ["ServiceToken:ClientId"] = "service-orders",
            ["ServiceToken:ClientSecret"] = "secret",
        }).Build();
        ServiceCollection services = new();
        services.AddPlatformServiceToken(configuration);
        services.AddHttpClient(ServiceTokenProvider.HTTP_CLIENT).ConfigurePrimaryHttpMessageHandler(() => keycloak);
        return services.BuildServiceProvider();
    }

    private static HttpClient Client(ServiceProvider services, StubHandler api) =>
        new(new ServiceTokenHandler(services.GetRequiredService<IServiceTokenProvider>()) { InnerHandler = api });

    private static HttpResponseMessage Token(string value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"access_token":"{{value}}","expires_in":300}""", Encoding.UTF8, "application/json"),
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        /// <summary>Токен в момент отправки: при повторе тот же объект запроса получает новый заголовок.</summary>
        public List<string?> Tokens { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Tokens.Add(request.Headers.Authorization?.Parameter);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }
}
