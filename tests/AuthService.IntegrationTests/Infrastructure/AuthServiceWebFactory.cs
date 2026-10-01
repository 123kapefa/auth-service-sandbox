using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using AuthService.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PlatformAuth;
using PlatformAuth.Revocation;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>
/// Весь сервис в памяти. Keycloak не нужен: ключи подписи подменены тестовым RSA-ключом,
/// остальная проверка токена (iss, aud, срок, RS256), политики и права — настоящие.
/// </summary>
public sealed class AuthServiceWebFactory : WebApplicationFactory<Program>
{
    // Как в appsettings.Development.json: тесты проверяют ту же конфигурацию, с которой сервис запускается.
    public const string ISSUER = "http://localhost:8180/realms/sandbox";
    public const string AUDIENCE = "sandbox-api";

    // Порт 1 никто не слушает: база гарантированно недоступна, тесты не зависят от Docker.
    private const string UNREACHABLE_DATABASE = "Host=127.0.0.1;Port=1;Database=auth_service;Username=test;Password=test;Timeout=1";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _signingKey;

    public AuthServiceWebFactory() => _signingKey = new RsaSecurityKey(_rsa) { KeyId = "test-key" };

    /// <summary>Подставной сервер входа вместо Keycloak.</summary>
    public FakeIdentityProvider IdentityProvider { get; } = new();

    /// <summary>Отметки об отзыве в памяти вместо Redis.</summary>
    public InMemoryTokenRevocations Revocations { get; } = new();

    /// <summary>Журнал в памяти вместо Postgres.</summary>
    public InMemoryAuditLog AuditLog { get; } = new();

    /// <summary>Подставной Admin API вместо Keycloak.</summary>
    public FakeIdentityAdmin IdentityAdmin { get; } = new();

    /// <summary>Клиент как браузер без автоматических переходов и cookie: тест видит каждый 302 и Set-Cookie.</summary>
    public HttpClient CreateBrowserClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    /// <summary>Клиент с access-токеном пользователя; null — анонимный клиент. authenticatedAt — когда «вошёл» (по умолчанию — только что).</summary>
    public HttpClient CreateClientFor(TestUser? user, DateTimeOffset? authenticatedAt = null, string? sessionId = null)
    {
        HttpClient client = CreateClient();
        if (user is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(user, authenticatedAt, sessionId));
        }

        return client;
    }

    /// <summary>Клиент с заранее выпущенным токеном (например, «старым» — до отзыва).</summary>
    public HttpClient CreateClientWithToken(string token)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    public string CreateToken(
        TestUser user,
        DateTimeOffset? authenticatedAt = null,
        string? sessionId = null,
        DateTimeOffset? issuedAt = null,
        string authorizedParty = "sandbox-bff",
        IReadOnlyDictionary<string, object>? extraClaims = null)
    {
        ArgumentNullException.ThrowIfNull(user);

        DateTime now = (issuedAt ?? DateTimeOffset.UtcNow).UtcDateTime;
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [AuthClaims.SUBJECT] = user.Id.ToString(),
            [AuthClaims.PHONE] = user.Phone,
            [AuthClaims.ROLE] = user.Roles.ToArray(),
            [AuthClaims.AVAILABLE_ROLES] = user.Roles.ToArray(),
            [AuthClaims.ACTIVE_MODE] = user.ActiveMode,
            [AuthClaims.PERMISSIONS] = RealmPermissions.Of(user.ActiveMode, user.Roles),
            [AuthClaims.AUTH_TIME] = (authenticatedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
            [AuthClaims.SESSION_ID] = sessionId ?? $"session-{user.Id:N}",
            [AuthClaims.AUTHORIZED_PARTY] = authorizedParty,
        };
        foreach ((string name, object value) in extraClaims ?? new Dictionary<string, object>())
        {
            claims[name] = value;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ISSUER,
            Audience = AUDIENCE,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(15), // как access-токен Keycloak
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
            Claims = claims,
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:AuthDb", UNREACHABLE_DATABASE);
        // Проверка отзыва без кэша: тест проверяет запрос сразу после отметки.
        builder.UseSetting("TokenRevocation:LocalCache", "00:00:00");
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = ISSUER };
                configuration.SigningKeys.Add(_signingKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
            services.RemoveAll<IIdentityProvider>();
            services.AddSingleton<IIdentityProvider>(IdentityProvider);
            services.RemoveAll<IAuditLog>();
            services.AddSingleton<IAuditLog>(AuditLog);
            services.RemoveAll<ITokenRevocationStore>();
            services.AddSingleton<ITokenRevocationStore>(Revocations);
            services.RemoveAll<IIdentityAdmin>();
            services.AddSingleton<IIdentityAdmin>(IdentityAdmin);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rsa.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Тестовый пользователь — как пользователи realm sandbox.</summary>
public sealed record TestUser(string Name, Guid Id, string Phone, IReadOnlyList<string> Roles, string ActiveMode)
{
    public static TestUser Customer { get; } =
        new("покупатель", Guid.NewGuid(), "+79990000001", [AuthModes.CUSTOMER], AuthModes.CUSTOMER);

    public static TestUser AdminInCustomerMode { get; } =
        new("админ в режиме customer", Guid.NewGuid(), "+79990000002", [AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.CUSTOMER);

    public static TestUser ManagerInCustomerMode { get; } =
        new("менеджер в режиме customer", Guid.NewGuid(), "+79990000003", [AuthModes.CUSTOMER, AuthModes.MANAGER], AuthModes.CUSTOMER);

    public static TestUser AdminInAdminMode { get; } =
        new("админ в режиме admin", Guid.NewGuid(), "+71234567890", [AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN);

    public static TestUser ManagerInManagerMode { get; } =
        new("менеджер в режиме manager", Guid.NewGuid(), "+70987654321", [AuthModes.CUSTOMER, AuthModes.MANAGER], AuthModes.MANAGER);

    public override string ToString() => Name;
}
