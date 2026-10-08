using System.Data.Common;
using AuthService.Infrastructure.Keycloak;
using AuthService.Infrastructure.Postgres;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PlatformAuth;
using PlatformAuth.Redis;
using Respawn;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace AuthService.InfrastructureTests.Infrastructure;

/// <summary>
/// Postgres, Redis и Keycloak в контейнерах — одни на все тесты набора (Keycloak стартует долго). Сервисы — настоящие
/// регистрации инфраструктуры (AddInfrastructurePostgres, AddPlatformTokenRevocation, AddInfrastructureKeycloak), как
/// в сервисе. Между тестами база чистится Respawn, Redis — FLUSHDB; в Keycloak каждый тест заводит своих пользователей,
/// роли и клиенты с уникальными именами и убирает их за собой — realm из infra/keycloak/realm общий.
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    public const string REALM = "sandbox";
    public const string BFF_CLIENT = "sandbox-bff";
    public const string BFF_SECRET = "infrastructure-tests-bff-secret";
    public const string API_CLIENT = "sandbox-api";

    /// <summary>Пароль пользователей realm JSON (как в scripts/keycloak/lib.sh).</summary>
    public const string REALM_PASSWORD = "Test1234!";

    private const string KEYCLOAK_ADMIN = "admin";
    private const string KEYCLOAK_ADMIN_PASSWORD = "admin";
    private const int KEYCLOAK_PORT = 8080;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private IContainer? _keycloak;
    private Respawner _respawner = null!;

    public ServiceProvider Services { get; private set; } = null!;

    public KeycloakMaster Master { get; private set; } = null!;

    /// <summary>Внешний адрес Keycloak (порт на машине): http://127.0.0.1:{порт}/.</summary>
    public Uri KeycloakAddress { get; private set; } = null!;

    public string Issuer => new Uri(KeycloakAddress, $"realms/{REALM}").ToString();

    public async Task InitializeAsync()
    {
        string image = await KeycloakImage.ResolveAsync();
        _keycloak = new ContainerBuilder(image)
            .WithCommand("start-dev", "--import-realm")
            .WithResourceMapping(new FileInfo(RepositoryRoot.Path("infra/keycloak/realm/sandbox-realm.json")), "/opt/keycloak/data/import/")
            .WithEnvironment(KeycloakEnvironment())
            .WithPortBinding(KEYCLOAK_PORT, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(KEYCLOAK_PORT)
                .ForPath($"/realms/{REALM}/.well-known/openid-configuration")))
            .Build();

        IContainer keycloak = _keycloak;
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), keycloak.StartAsync());

        KeycloakAddress = new UriBuilder(Uri.UriSchemeHttp, keycloak.Hostname, keycloak.GetMappedPublicPort(KEYCLOAK_PORT)).Uri;
        Master = new KeycloakMaster(KeycloakAddress, REALM, KEYCLOAK_ADMIN, KEYCLOAK_ADMIN_PASSWORD);
        string registrationToken = await Master.CreateRegistrationTokenAsync(count: 100);

        Services = BuildServices(registrationToken);

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
        }

        await using DbConnection connection = await OpenDatabaseAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = [AuthDbContext.SCHEMA],
            TablesToIgnore = [new Respawn.Graph.Table(AuthDbContext.SCHEMA, AuthDbContext.MIGRATIONS_HISTORY_TABLE)],
        });
    }

    public async Task DisposeAsync()
    {
        if (Services is not null)
        {
            await Services.DisposeAsync();
        }

        Master?.Dispose();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        if (_keycloak is not null)
        {
            await _keycloak.DisposeAsync();
        }
    }

    /// <summary>Пустые таблицы схемы auth (история миграций остаётся).</summary>
    public async Task ResetDatabaseAsync()
    {
        await using DbConnection connection = await OpenDatabaseAsync();
        await _respawner.ResetAsync(connection);
    }

    /// <summary>Пустой Redis.</summary>
    public async Task ResetRedisAsync()
    {
        await using ConnectionMultiplexer admin = await ConnectionMultiplexer.ConnectAsync($"{_redis.GetConnectionString()},allowAdmin=true");
        await admin.GetServer(admin.GetEndPoints()[0]).FlushDatabaseAsync();
    }

    public async Task<NpgsqlConnection> OpenDatabaseAsync()
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private ServiceProvider BuildServices(string registrationToken)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:AuthDb"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
                // Без локального кэша: тест сразу видит, что записал в Redis.
                ["TokenRevocation:LocalCache"] = "00:00:00",
                ["Authentication:Authority"] = Issuer,
                ["Authentication:Audience"] = API_CLIENT,
                ["Authentication:RequireHttpsMetadata"] = "false",
                ["Keycloak:ClientId"] = BFF_CLIENT,
                ["Keycloak:ClientSecret"] = BFF_SECRET,
                ["Keycloak:RegistrationToken"] = registrationToken,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.Configure<JwtAuthenticationOptions>(configuration.GetSection(JwtAuthenticationOptions.SECTION_NAME));
        services.AddInfrastructurePostgres(configuration);
        services.AddPlatformTokenRevocation(configuration);
        services.AddInfrastructureKeycloak(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    // Как в compose.yaml, но без своей базы (start-dev — встроенная) и без внешних адресов: тестам они не нужны.
    private static Dictionary<string, string> KeycloakEnvironment() => new(StringComparer.Ordinal)
    {
        ["KC_BOOTSTRAP_ADMIN_USERNAME"] = KEYCLOAK_ADMIN,
        ["KC_BOOTSTRAP_ADMIN_PASSWORD"] = KEYCLOAK_ADMIN_PASSWORD,
        ["KC_HTTP_PORT"] = KEYCLOAK_PORT.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["SANDBOX_BFF_CLIENT_SECRET"] = BFF_SECRET,
        ["LEGACY_IMPORT_CLIENT_SECRET"] = "infrastructure-tests-legacy-secret",
        ["ESIA_CLIENT_ID"] = "SANDBOX",
        ["ESIA_AUTHORIZATION_URL"] = "http://idp-stub.invalid/esia/aas/oauth2/v2/ac",
        ["ESIA_TOKEN_URL"] = "http://idp-stub.invalid/esia/aas/oauth2/v3/te",
        ["ESIA_API_URL"] = "http://idp-stub.invalid/esia",
        ["ESIA_SIGNER_URL"] = "http://idp-stub.invalid/sign",
        ["ESIA_CERTIFICATE_HASH"] = "sandbox-certificate-hash",
        ["YANDEX_CLIENT_ID"] = "sandbox-yandex",
        ["YANDEX_CLIENT_SECRET"] = "sandbox-yandex-secret",
        ["YANDEX_AUTHORIZATION_URL"] = "http://idp-stub.invalid/yandex/authorize",
        ["YANDEX_TOKEN_URL"] = "http://idp-stub.invalid/yandex/token",
        ["YANDEX_USER_INFO_URL"] = "http://idp-stub.invalid/yandex/info?format=json",
        // Телефония стенда не нужна: коды пишутся в лог Keycloak.
        ["KC_SPI_AUTHENTICATOR__PLATFORM_CODE_LOGIN__PHONE_PROVIDER_URL"] = string.Empty,
    };
}

/// <summary>Все тесты набора делят одни контейнеры и идут друг за другом.</summary>
[CollectionDefinition(NAME)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string NAME = "Infrastructure";
}

/// <summary>Категория тестов на настоящей инфраструктуре: в CI — отдельная задача, быстрый прогон её исключает.</summary>
public static class TestCategories
{
    public const string INFRASTRUCTURE = "Infrastructure";
}
