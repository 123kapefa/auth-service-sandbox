using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using AuthService.Api.Endpoints;
using AuthService.Api.Options;
using AuthService.Api.Services;
using AuthService.Core.Features.Accounts.Register;
using AuthService.Infrastructure.Postgres;
using AuthService.Infrastructure.Postgres.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OidcOptions>(builder.Configuration.GetSection(OidcOptions.SectionName));
builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.SectionName));
builder.Services.AddPostgresInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<BootstrapHostedService>();
builder.Services.AddScoped<RegisterAccountHandler>();
builder.Services.AddScoped<ClientCredentialsTokenHandler>();
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("UserInfo", policy =>
    {
        policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("AdminMfa", policy =>
    {
        policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            context.User.IsInRole("platform_admin") &&
            context.User.HasClaim("amr", "mfa") &&
            context.User.HasScope("auth.admin"));
    });
});

var oidcOptions = builder.Configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();
builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore()
        .UseDbContext<AuthDbContext>()
        .ReplaceDefaultEntities<OidcApplication, OidcAuthorization, OidcScope, OidcToken, Guid>())
    .AddServer(options =>
    {
        options.SetIssuer(new Uri(oidcOptions.Issuer, UriKind.Absolute));
        options.SetAuthorizationEndpointUris("/connect/authorize")
            .SetTokenEndpointUris("/connect/token")
            .SetUserInfoEndpointUris("/connect/userinfo")
            .SetEndSessionEndpointUris("/connect/logout")
            .SetRevocationEndpointUris("/connect/revocation");
        options.AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow()
            .AllowClientCredentialsFlow()
            .RequireProofKeyForCodeExchange();
        options.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.Roles, Scopes.OfflineAccess, "auth.admin", "demo.api");
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
        options.SetIdentityTokenLifetime(TimeSpan.FromMinutes(5));
        options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(5));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));
        options.DisableAccessTokenEncryption();
        ConfigureCredentials(options, oidcOptions, builder.Environment);
        options.AddEventHandler<OpenIddict.Server.OpenIddictServerEvents.HandleTokenRequestContext>(handler =>
            handler.UseScopedHandler<ClientCredentialsTokenHandler>());
        var aspNetCore = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough()
            .EnableUserInfoEndpointPassthrough()
            .EnableStatusCodePagesIntegration();
        if (builder.Environment.IsDevelopment())
        {
            aspNetCore.DisableTransportSecurityRequirement();
        }
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/Error");
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live");
app.MapGet("/health/ready", async (AuthDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
app.MapRazorPages().RequireRateLimiting("auth");
app.MapControllers();
app.MapOpenIdConnectEndpoints();
app.MapAdministrationEndpoints();
app.MapGet("/", () => Results.Redirect("/Account/Login"));

app.Run();

static void ConfigureCredentials(
    OpenIddictServerBuilder options,
    OidcOptions settings,
    IHostEnvironment environment)
{
    if (!string.IsNullOrWhiteSpace(settings.Keys.SigningCertificatePath) &&
        !string.IsNullOrWhiteSpace(settings.Keys.EncryptionCertificatePath))
    {
        options.AddSigningCertificate(LoadCertificate(settings.Keys.SigningCertificatePath, settings.Keys.SigningCertificatePassword));
        if (!string.IsNullOrWhiteSpace(settings.Keys.PreviousSigningCertificatePath))
        {
            options.AddSigningCertificate(LoadCertificate(settings.Keys.PreviousSigningCertificatePath, settings.Keys.PreviousSigningCertificatePassword));
        }
        options.AddEncryptionCertificate(LoadCertificate(settings.Keys.EncryptionCertificatePath, settings.Keys.EncryptionCertificatePassword));
        return;
    }

    if (!environment.IsDevelopment())
    {
        throw new InvalidOperationException("Production requires Oidc:Keys signing and encryption certificates.");
    }

    options.AddDevelopmentSigningCertificate();
    options.AddDevelopmentEncryptionCertificate();
}

static X509Certificate2 LoadCertificate(string path, string? password) =>
    X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);

public partial class Program;
