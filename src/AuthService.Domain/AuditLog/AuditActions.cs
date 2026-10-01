namespace AuthService.Domain.AuditLog;

/// <summary>Действия в журнале. Первые два — как в боевом сервисе.</summary>
public static class AuditActions
{
    public const string USER_RESTRICTION_CHANGED = "UserRestrictionChanged";
    public const string USER_ROLE_CHANGED = "UserRoleChanged";
    public const string USER_APP_ACCESS_CHANGED = "UserAppAccessChanged";
    public const string USER_LOGGED_OUT_EVERYWHERE = "UserLoggedOutEverywhere";

    public const string USER_OBJECT = "user";
}
