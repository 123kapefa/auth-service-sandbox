using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PlatformAuth;
using Xunit;

namespace Shared.UnitTests;

public sealed class JwtAuthenticationOptionsTests
{
    private const string AUTHORITY = "http://localhost:8180/realms/sandbox";

    private readonly JwtAuthenticationOptionsValidator _validator = new();

    [Fact]
    public void Binding_ReadsSectionAndDefaultsIssuerToAuthority()
    {
        JwtAuthenticationOptions options = Bind(new()
        {
            ["Authentication:Authority"] = AUTHORITY,
            ["Authentication:Audience"] = "sandbox-api",
            ["Authentication:RequireHttpsMetadata"] = "false",
        });

        Assert.Equal(AUTHORITY, options.Authority);
        Assert.Equal("sandbox-api", options.Audience);
        Assert.False(options.RequireHttpsMetadata);
        Assert.Equal(AUTHORITY, options.ResolveIssuer());
        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void DockerStyle_InternalMetadataAndExternalIssuer_IsValid()
    {
        var options = new JwtAuthenticationOptions
        {
            Authority = "http://keycloak:8180/realms/sandbox",
            MetadataAddress = "http://keycloak:8180/realms/sandbox/.well-known/openid-configuration",
            ValidIssuer = AUTHORITY,
            Audience = "sandbox-api",
            RequireHttpsMetadata = false,
        };

        Assert.True(_validator.Validate(null, options).Succeeded);
        Assert.Equal(AUTHORITY, options.ResolveIssuer());
    }

    [Fact]
    public void RequireHttpsMetadata_IsTrueByDefault() =>
        Assert.True(new JwtAuthenticationOptions().RequireHttpsMetadata);

    [Fact]
    public void Empty_FailsWithAuthorityAndAudience()
    {
        ValidateOptionsResult result = _validator.Validate(null, new JwtAuthenticationOptions());

        Assert.True(result.Failed);
        Assert.Contains("Authentication:Authority is required.", result.Failures!);
        Assert.Contains("Authentication:Audience is required.", result.Failures!);
    }

    [Fact]
    public void HttpAuthority_WithRequireHttps_Fails()
    {
        ValidateOptionsResult result = _validator.Validate(null, new JwtAuthenticationOptions
        {
            Authority = AUTHORITY,
            Audience = "sandbox-api",
        });

        string failure = Assert.Single(result.Failures!);
        Assert.Contains("must use https", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("keycloak:8180/realms")]   // забыт http:// — .NET считает это схемой keycloak:
    [InlineData("/realms/sandbox")]
    public void NotHttpUrlMetadataAddress_Fails(string metadataAddress)
    {
        ValidateOptionsResult result = _validator.Validate(null, new JwtAuthenticationOptions
        {
            Authority = AUTHORITY,
            MetadataAddress = metadataAddress,
            Audience = "sandbox-api",
            RequireHttpsMetadata = false,
        });

        string failure = Assert.Single(result.Failures!);
        Assert.Contains("Authentication:MetadataAddress must be an absolute URL", failure, StringComparison.Ordinal);
    }

    private static JwtAuthenticationOptions Bind(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build()
            .GetSection(JwtAuthenticationOptions.SECTION_NAME)
            .Get<JwtAuthenticationOptions>()!;
}
