using Microsoft.AspNetCore.Identity;

namespace AuthService.Domain.Identity;

public sealed class Account : IdentityUser<Guid>
{
    private Account()
    {
    }

    public Account(string email, string userName, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Id = Guid.CreateVersion7();
        Email = email.Trim();
        UserName = userName.Trim();
        DisplayName = displayName.Trim();
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public string DisplayName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdateDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
