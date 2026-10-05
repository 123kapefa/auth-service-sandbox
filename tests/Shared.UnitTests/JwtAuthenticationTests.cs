using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PlatformAuth;
using Xunit;

namespace Shared.UnitTests;

// Настоящие JWT, подписанные тестовым RSA-ключом, проверяются теми параметрами, что собирает AddPlatformJwtAuthentication.
public sealed class JwtAuthenticationTests : IDisposable
{
    private const string ISSUER = "http://localhost:8180/realms/sandbox";
    private const string AUDIENCE = "sandbox-api";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly JwtBearerOptions _bearer;

    public JwtAuthenticationTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-key" };
        _bearer = BuildBearerOptions(new()
        {
            ["Authentication:Authority"] = "http://keycloak:8180/realms/sandbox",
            ["Authentication:MetadataAddress"] = "http://keycloak:8180/realms/sandbox/.well-known/openid-configuration",
            ["Authentication:ValidIssuer"] = ISSUER,
            ["Authentication:Audience"] = AUDIENCE,
            ["Authentication:RequireHttpsMetadata"] = "false",
            ["Authentication:AccessTokenCookie"] = AuthCookies.ACCESS_TOKEN,
        });
    }

    [Fact]
    public void Options_AreTakenFromSection()
    {
        Assert.Equal("http://keycloak:8180/realms/sandbox/.well-known/openid-configuration", _bearer.MetadataAddress);
        Assert.False(_bearer.RequireHttpsMetadata);
        Assert.False(_bearer.MapInboundClaims);
        Assert.Equal(ISSUER, _bearer.TokenValidationParameters.ValidIssuer);
        Assert.Equal(AUDIENCE, _bearer.TokenValidationParameters.ValidAudience);
    }

    [Fact]
    public async Task ValidToken_Accepted_RolesAndSubjectReadable()
    {
        TokenValidationResult result = await ValidateAsync(CreateToken());

        Assert.True(result.IsValid, result.Exception?.Message);
        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        Assert.Equal("user-1", principal.Identity!.Name);
        Assert.True(principal.IsInRole(AuthModes.ADMIN));
        Assert.True(principal.IsInRole(AuthModes.CUSTOMER));
        Assert.False(principal.IsInRole(AuthModes.MANAGER));
        Assert.Equal(AuthModes.ADMIN, principal.FindFirstValue(AuthClaims.ACTIVE_MODE));
    }

    [Fact]
    public async Task WrongAudience_Rejected() =>
        Assert.False((await ValidateAsync(CreateToken(audience: "other-api"))).IsValid);

    [Fact]
    public async Task WrongIssuer_Rejected() =>
        Assert.False((await ValidateAsync(CreateToken(issuer: "http://keycloak:8180/realms/sandbox"))).IsValid);

    [Fact]
    public async Task ExpiredToken_Rejected() =>
        Assert.False((await ValidateAsync(CreateToken(expires: DateTime.UtcNow.AddSeconds(-1)))).IsValid);

    [Fact]
    public async Task SymmetricHs256Token_Rejected()
    {
        var sharedSecret = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));

        TokenValidationResult result = await ValidateAsync(
            CreateToken(credentials: new SigningCredentials(sharedSecret, SecurityAlgorithms.HmacSha256)),
            extraKey: sharedSecret);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Cookie_UsedWhenNoAuthorizationHeader() =>
        Assert.Equal("token-from-cookie", await ReceiveTokenAsync(_bearer, cookie: "token-from-cookie"));

    [Fact]
    public async Task AuthorizationHeader_WinsOverCookie() =>
        Assert.Null(await ReceiveTokenAsync(_bearer, cookie: "token-from-cookie", authorizationHeader: "Bearer from-header"));

    [Fact]
    public async Task NoCookie_NoToken() =>
        Assert.Null(await ReceiveTokenAsync(_bearer, cookie: null));

    [Fact]
    public async Task CookieNotConfigured_HeaderOnly()
    {
        // По умолчанию — только заголовок: cookie сервиса входа чужому сервису не нужна.
        JwtBearerOptions byDefault = BuildBearerOptions(new()
        {
            ["Authentication:Authority"] = ISSUER,
            ["Authentication:Audience"] = AUDIENCE,
            ["Authentication:RequireHttpsMetadata"] = "false",
        });

        Assert.Null(await ReceiveTokenAsync(byDefault, cookie: "token-from-cookie"));
    }

    [Fact]
    public void InternalAuthority_GivesMetadataAddress_IssuerStaysAuthority()
    {
        JwtBearerOptions inside = BuildBearerOptions(new()
        {
            ["Authentication:Authority"] = ISSUER,
            ["Authentication:InternalAuthority"] = "http://keycloak:8180/realms/sandbox/",
            ["Authentication:Audience"] = AUDIENCE,
            ["Authentication:RequireHttpsMetadata"] = "false",
        });

        Assert.Equal("http://keycloak:8180/realms/sandbox/.well-known/openid-configuration", inside.MetadataAddress);
        Assert.Equal(ISSUER, inside.TokenValidationParameters.ValidIssuer);
    }

    [Fact]
    public async Task EmptyCookieSetting_DisablesCookie()
    {
        JwtBearerOptions headerOnly = BuildBearerOptions(new()
        {
            ["Authentication:Authority"] = ISSUER,
            ["Authentication:Audience"] = AUDIENCE,
            ["Authentication:RequireHttpsMetadata"] = "false",
            ["Authentication:AccessTokenCookie"] = string.Empty,
        });

        Assert.Null(await ReceiveTokenAsync(headerOnly, cookie: "token-from-cookie"));
    }

    public void Dispose() => _rsa.Dispose();

    // Токен, который обработчик возьмёт из запроса. null — обработчик сам прочитает заголовок (или токена нет).
    private static async Task<string?> ReceiveTokenAsync(JwtBearerOptions bearer, string? cookie, string? authorizationHeader = null)
    {
        var httpContext = new DefaultHttpContext();
        if (cookie is not null)
        {
            httpContext.Request.Headers.Cookie = $"{AuthCookies.ACCESS_TOKEN}={cookie}";
        }

        if (authorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        var context = new MessageReceivedContext(
            httpContext,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            bearer);
        await bearer.Events.MessageReceived(context);

        return context.Token;
    }

    private string CreateToken(
        string issuer = ISSUER,
        string audience = AUDIENCE,
        DateTime? expires = null,
        SigningCredentials? credentials = null)
    {
        DateTime notBefore = DateTime.UtcNow.AddMinutes(-5);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore,
            IssuedAt = notBefore,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = credentials ?? new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [AuthClaims.SUBJECT] = "user-1",
                [AuthClaims.ROLE] = new[] { AuthModes.CUSTOMER, AuthModes.ADMIN },
                [AuthClaims.ACTIVE_MODE] = AuthModes.ADMIN,
            },
        });
    }

    private Task<TokenValidationResult> ValidateAsync(string token, SecurityKey? extraKey = null)
    {
        // Ключи подставляем вместо скачивания из discovery — остальные параметры те, что собрало расширение.
        TokenValidationParameters parameters = _bearer.TokenValidationParameters.Clone();
        parameters.IssuerSigningKeys = extraKey is null ? [_key] : [_key, extraKey];
        return new JsonWebTokenHandler { MapInboundClaims = _bearer.MapInboundClaims }.ValidateTokenAsync(token, parameters);
    }

    private static JwtBearerOptions BuildBearerOptions(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceProvider provider = new ServiceCollection()
            .AddLogging()
            .AddPlatformJwtAuthentication(configuration)
            .BuildServiceProvider();

        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
    }
}
