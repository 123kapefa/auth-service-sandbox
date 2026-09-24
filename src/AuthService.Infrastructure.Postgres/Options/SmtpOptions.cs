namespace AuthService.Infrastructure.Postgres.Options;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = "mailpit";

    public int Port { get; init; } = 1025;

    public string FromAddress { get; init; } = "no-reply@localhost";

    public string FromName { get; init; } = "Auth Service";

    public string? UserName { get; init; }

    public string? Password { get; init; }

    public bool EnableSsl { get; init; }
}
