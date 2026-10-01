using AuthService.Domain.AuditLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Postgres.AuditLog;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever(); // GUID v7 — из домена, по времени записи
        builder.Property(entry => entry.Action).HasMaxLength(AuditEntry.MAX_ACTION_LENGTH).IsRequired();
        builder.Property(entry => entry.ObjectType).HasMaxLength(AuditEntry.MAX_ACTION_LENGTH).IsRequired();
        builder.Property(entry => entry.ObjectName).HasMaxLength(AuditEntry.MAX_NAME_LENGTH);
        builder.Property(entry => entry.Changes).HasColumnType("jsonb").IsRequired();
        builder.Property(entry => entry.ActorName).HasMaxLength(AuditEntry.MAX_NAME_LENGTH);
        builder.Property(entry => entry.ActorRole).HasMaxLength(AuditEntry.MAX_ACTION_LENGTH);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(AuditEntry.MAX_ACTION_LENGTH);

        // Типовые выборки: «что делали с пользователем» и «что делал сотрудник», новые сверху.
        builder.HasIndex(entry => new { entry.ObjectId, entry.OccurredAt });
        builder.HasIndex(entry => new { entry.ActorId, entry.OccurredAt });
        builder.HasIndex(entry => entry.OccurredAt);
    }
}
