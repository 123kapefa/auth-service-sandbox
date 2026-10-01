namespace PlatformAuth.Redis;

/// <summary>
/// Теги записей кэша проверки: по ним экземпляр, поставивший отметку, сразу сбрасывает свой кэш — иначе только что
/// проверенный токен (например, тем же запросом выхода) ещё до LocalCache считался бы действующим.
/// </summary>
internal static class RevocationCacheTags
{
    public static string User(string subject) => $"auth:revocation:user:{subject}";

    public static string Session(string sessionId) => $"auth:revocation:session:{sessionId}";

    public static string[] For(string subject, string? sessionId) =>
        sessionId is null ? [User(subject)] : [User(subject), Session(sessionId)];
}
