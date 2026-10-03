using AuthService.Domain.ServiceKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Postgres.ServiceKeys;

public sealed class ServiceKeyConfiguration : IEntityTypeConfiguration<ServiceKey>
{
    public void Configure(EntityTypeBuilder<ServiceKey> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("service_keys");
        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).ValueGeneratedNever(); // GUID v7 — из домена
        builder.Property(key => key.Code).HasMaxLength(ServiceKey.MAX_CODE_LENGTH).IsRequired();
        builder.Property(key => key.ClientId).HasMaxLength(ServiceKey.MAX_CODE_LENGTH + ServiceKey.CLIENT_ID_PREFIX.Length).IsRequired();
        builder.Property(key => key.Name).HasMaxLength(ServiceKey.MAX_NAME_LENGTH).IsRequired();
        builder.Property(key => key.ProtectedRegistrationToken).IsRequired();
        builder.HasIndex(key => key.Code).IsUnique();
        builder.HasIndex(key => key.ClientId).IsUnique();
    }
}
