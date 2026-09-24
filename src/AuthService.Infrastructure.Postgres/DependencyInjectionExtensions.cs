using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using AuthService.Infrastructure.Postgres.Audit;
using AuthService.Infrastructure.Postgres.Email;
using AuthService.Infrastructure.Postgres.Identity;
using AuthService.Infrastructure.Postgres.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.EntityFrameworkCore;

namespace AuthService.Infrastructure.Postgres;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddPostgresInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("AuthDb")
            ?? throw new InvalidOperationException("ConnectionStrings:AuthDb is required.");

        services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
            options.UseOpenIddict<OidcApplication, OidcAuthorization, OidcScope, OidcToken, Guid>();
        });

        services.AddDataProtection()
            .SetApplicationName("auth-service")
            .PersistKeysToDbContext<AuthDbContext>();

        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<ISecurityAuditWriter, PostgresSecurityAuditWriter>();

        services.AddIdentity<Account, Role>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/Login";
            options.Cookie.Name = "__Host-auth-session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });

        return services;
    }
}
