using AuthService.Infrastructure.Postgres.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OpenIddict.EntityFrameworkCore;

namespace AuthService.Infrastructure.Postgres;

public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUTH_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=auth_service;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AuthDbContext>();
        options.UseNpgsql(connectionString);
        options.UseSnakeCaseNamingConvention();
        options.UseOpenIddict<OidcApplication, OidcAuthorization, OidcScope, OidcToken, Guid>();
        return new AuthDbContext(options.Options);
    }
}
