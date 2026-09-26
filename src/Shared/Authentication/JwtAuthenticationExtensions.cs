using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PlatformAuth;

public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Проверка access-токенов сервера входа (Keycloak): подпись по ключам из discovery (RS256),
    /// iss, aud, срок действия. Настройки — секция Authentication, проверяются при старте.
    /// </summary>
    public static IServiceCollection AddPlatformJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtAuthenticationOptions>()
            .Bind(configuration.GetSection(JwtAuthenticationOptions.SECTION_NAME))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtAuthenticationOptions>, JwtAuthenticationOptionsValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtAuthenticationOptions>>((bearer, settings) => Configure(bearer, settings.Value));

        return services;
    }

    private static void Configure(JwtBearerOptions bearer, JwtAuthenticationOptions settings)
    {
        bearer.Authority = settings.Authority;
        if (!string.IsNullOrWhiteSpace(settings.MetadataAddress))
        {
            bearer.MetadataAddress = settings.MetadataAddress;
        }

        bearer.RequireHttpsMetadata = settings.RequireHttpsMetadata;

        // Claims остаются как в токене (role, sub), без переименования в длинные URI .NET.
        bearer.MapInboundClaims = false;

        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.ResolveIssuer(),
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.Zero,
            NameClaimType = AuthClaims.SUBJECT,
            RoleClaimType = AuthClaims.ROLE,
        };
    }
}
