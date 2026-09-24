namespace AuthService.Api.Options;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public bool Enabled { get; init; }

    public string? AdminEmail { get; init; }

    public string? AdminUserName { get; init; }

    public string? AdminPassword { get; init; }

    public string? DevServiceSecret { get; init; }
}
