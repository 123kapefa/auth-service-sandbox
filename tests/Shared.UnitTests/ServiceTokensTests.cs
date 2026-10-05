using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    public async Task Retry_IsInNextSecond_AndResendsBody()
    {
        int issued = 0;
        StubHandler keycloak = new(_ => Token($"t{++issued}"));
        List<(long Second, string Body)> seen = [];
        StubHandler api = new(request =>
        {
            seen.Add((DateTimeOffset.UtcNow.ToUnixTimeSeconds(), request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()));
            return new HttpResponseMessage(seen.Count == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
        });
        using ServiceProvider services = Services(keycloak);
        using HttpClient client = Client(services, api);

        using StringContent body = new("""{"text":"заказ оформлен"}""", Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync(new Uri("http://notifications/send"), body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(seen[1].Second > seen[0].Second, "повтор — в следующей секунде");
        Assert.Equal(seen[0].Body, seen[1].Body);
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

    [Fact]
    public async Task SecretFile_IsReread_ForEachNewToken()
    {
        // Плановый перевыпуск: новый секрет положили в файл — следующий токен берётся уже с ним, без перезапуска.
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "old-secret\n");
            StubHandler keycloak = new(_ => Token("t"));
            using ServiceProvider services = Services(keycloak, new()
            {
                ["ServiceToken:ClientSecret"] = null,
                ["ServiceToken:ClientSecretFile"] = file,
            });
            IServiceTokenProvider tokens = services.GetRequiredService<IServiceTokenProvider>();

            tokens.Invalidate(await tokens.GetTokenAsync(default));
            await File.WriteAllTextAsync(file, "new-secret");
            await tokens.GetTokenAsync(default);

            Assert.Contains("client_secret=old-secret", keycloak.Bodies[0], StringComparison.Ordinal);
            Assert.Contains("client_secret=new-secret", keycloak.Bodies[1], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("http://keycloak:8180/realms/shop", "http://keycloak:8180/realms/shop/protocol/openid-connect/token")]
    [InlineData(null, "http://localhost:8180/realms/shop/protocol/openid-connect/token")]
    public void Authority_ComesFromAuthenticationSection(string? internalAuthority, string expected)
    {
        // Адрес сервера входа задают один раз (Authentication), ServiceToken — только ключ.
        using ServiceProvider services = Services(new StubHandler(_ => Token("t")), new()
        {
            ["ServiceToken:Authority"] = null,
            ["ServiceToken:TokenEndpoint"] = null,
            ["Authentication:Authority"] = "http://localhost:8180/realms/shop",
            ["Authentication:InternalAuthority"] = internalAuthority,
        });

        Assert.Equal(expected, services.GetRequiredService<IOptions<ServiceTokenOptions>>().Value.ResolveTokenEndpoint().ToString());
    }

    private static ServiceProvider Services(StubHandler keycloak, Dictionary<string, string?>? overrides = null)
    {
        Dictionary<string, string?> settings = new()
        {
            ["ServiceToken:Authority"] = "http://localhost:8180/realms/shop",
            ["ServiceToken:TokenEndpoint"] = TOKEN_URL,
            ["ServiceToken:ClientId"] = "service-orders",
            ["ServiceToken:ClientSecret"] = "secret",
        };
        foreach ((string key, string? value) in overrides ?? [])
        {
            settings[key] = value;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        services.AddPlatformServiceToken(configuration);
        services.AddHttpClient(ServiceTokenProvider.HTTP_CLIENT).ConfigurePrimaryHttpMessageHandler(() => keycloak);
        return services.BuildServiceProvider();
    }

    private static HttpClient Client(ServiceProvider services, StubHandler api) =>
        new(new ServiceTokenHandler(services.GetRequiredService<IServiceTokenProvider>(), TimeProvider.System) { InnerHandler = api });

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
