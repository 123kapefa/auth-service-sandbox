using PlatformAuth.Revocation;

namespace PlatformAuth.Redis;

/// <summary>
/// Теги записей кэша проверки: по ним экземпляр, поставивший отметку, сразу сбрасывает свой кэш — иначе только что
/// проверенный токен (например, тем же запросом выхода) ещё до LocalCache считался бы действующим.
/// </summary>
internal static class RevocationCacheTags
{
    public static string User(string subject) => $"auth:revocation:user:{subject}";

    public static string Session(string sessionId) => $"auth:revocation:session:{sessionId}";

    public static string Mode(string rolesClient, string role) => $"auth:revocation:mode:{rolesClient}:{role}";

    public static string[] For(TokenKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        List<string> tags = [User(key.Subject)];
        if (key.SessionId is not null)
        {
            tags.Add(Session(key.SessionId));
        }

        if (key.RolesClient is not null && key.ActiveMode is not null)
        {
            tags.Add(Mode(key.RolesClient, key.ActiveMode));
        }

        return [.. tags];
    }
}
