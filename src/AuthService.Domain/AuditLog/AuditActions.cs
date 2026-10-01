namespace AuthService.Domain.AuditLog;

/// <summary>Действия в журнале. Первые два — как в боевом сервисе.</summary>
public static class AuditActions
{
    public const string USER_RESTRICTION_CHANGED = "UserRestrictionChanged";
    public const string USER_ROLE_CHANGED = "UserRoleChanged";
    public const string USER_APP_ACCESS_CHANGED = "UserAppAccessChanged";
    public const string USER_LOGGED_OUT_EVERYWHERE = "UserLoggedOutEverywhere";

    public const string PERMISSION_CREATED = "PermissionCreated";
    public const string PERMISSION_DELETED = "PermissionDeleted";

    public const string ROLE_CREATED = "RoleCreated";
    public const string ROLE_UPDATED = "RoleUpdated";
    public const string ROLE_DELETED = "RoleDeleted";

    public const string USER_OBJECT = "user";
    public const string ROLE_OBJECT = "role";
    public const string PERMISSION_OBJECT = "permission";
}
