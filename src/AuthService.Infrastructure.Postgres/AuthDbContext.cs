using AuthService.Domain.AuditLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace AuthService.Infrastructure.Postgres;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public const string SCHEMA = "auth";

    /// <summary>Таблица истории миграций — одна и та же во время работы и в дизайн-тайме (иначе snapshot разъедется).</summary>
    public const string MIGRATIONS_HISTORY_TABLE = "__ef_migrations_history";

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>Общие настройки Npgsql: схема истории миграций и сборка с миграциями.</summary>
    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql)
    {
        ArgumentNullException.ThrowIfNull(npgsql);
        npgsql.MigrationsHistoryTable(MIGRATIONS_HISTORY_TABLE, SCHEMA);
        npgsql.MigrationsAssembly(typeof(AuthDbContext).Assembly.GetName().Name);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SCHEMA);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
