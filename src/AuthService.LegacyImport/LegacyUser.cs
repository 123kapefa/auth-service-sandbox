namespace AuthService.LegacyImport;

/// <summary>Строка таблицы users боевого auth-service (сущность User): только то, что переносится.</summary>
public sealed record LegacyUser(
    Guid Id,
    string Phone,
    string Role,
    bool IsRestricted,
    string? RestrictionReason,
    DateTimeOffset? RestrictedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    int LoginCount,
    string? PasswordHash,
    DateTimeOffset? PasswordSetAtUtc,
    DateTimeOffset CreatedAt);
