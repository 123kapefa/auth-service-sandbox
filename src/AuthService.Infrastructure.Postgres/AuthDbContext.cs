using AuthService.Domain.Audit;
using AuthService.Domain.Identity;
using AuthService.Infrastructure.Postgres.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore;

namespace AuthService.Infrastructure.Postgres;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<Account, Role, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    public DbSet<SecurityAuditEvent> SecurityAuditEvents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema("auth");
        builder.UseOpenIddict<OidcApplication, OidcAuthorization, OidcScope, OidcToken, Guid>();

        builder.Entity<Account>(entity =>
        {
            entity.ToTable("users");
            entity.Property(account => account.DisplayName).HasMaxLength(80);
            entity.Property(account => account.CreatedAt).IsRequired();
            entity.Property(account => account.UpdatedAt).IsRequired();
        });
        builder.Entity<Role>().ToTable("roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        builder.Entity<OidcApplication>().ToTable("oidc_applications");
        builder.Entity<OidcAuthorization>().ToTable("oidc_authorizations");
        builder.Entity<OidcScope>().ToTable("oidc_scopes");
        builder.Entity<OidcToken>().ToTable("oidc_tokens");

        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
        builder.Entity<SecurityAuditEvent>(entity =>
        {
            entity.ToTable("security_audit_events");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.EventType).HasMaxLength(120).IsRequired();
            entity.Property(entry => entry.ClientId).HasMaxLength(200);
            entity.Property(entry => entry.IpAddress).HasMaxLength(64);
            entity.Property(entry => entry.Metadata).HasMaxLength(4_000);
            entity.HasIndex(entry => new { entry.SubjectId, entry.OccurredAt });
            entity.HasIndex(entry => new { entry.EventType, entry.OccurredAt });
        });
    }
}
