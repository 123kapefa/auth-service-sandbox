using AuthService.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Postgres.Sessions;

public sealed class LoginDeviceConfiguration : IEntityTypeConfiguration<LoginDevice>
{
    public void Configure(EntityTypeBuilder<LoginDevice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("login_devices");
        builder.HasKey(device => device.SessionId);
        builder.Property(device => device.SessionId).HasMaxLength(LoginDevice.MAX_SESSION_ID_LENGTH).ValueGeneratedNever();
        builder.Property(device => device.UserAgent).HasMaxLength(LoginDevice.MAX_USER_AGENT_LENGTH);

        builder.HasIndex(device => device.UserId); // устройства сессий пользователя
        builder.HasIndex(device => device.CreatedAt); // удаление старше Retention
    }
}
