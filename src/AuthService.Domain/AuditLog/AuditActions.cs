namespace AuthService.Domain.AuditLog;

/// <summary>Действия в журнале. Первые два — как в боевом сервисе.</summary>
public static class AuditActions
{
    public const string USER_RESTRICTION_CHANGED = "UserRestrictionChanged";
    public const string USER_ROLE_CHANGED = "UserRoleChanged";
    public const string USER_APP_ACCESS_CHANGED = "UserAppAccessChanged";
    public const string USER_LOGGED_OUT_EVERYWHERE = "UserLoggedOutEverywhere";
    public const string USER_PASSWORD_RESET_REQUESTED = "UserPasswordResetRequested";
    public const string USER_TWO_FACTOR_DISABLED = "UserTwoFactorDisabled";

    public const string PERMISSION_CREATED = "PermissionCreated";
    public const string PERMISSION_DELETED = "PermissionDeleted";

    public const string ROLE_CREATED = "RoleCreated";
    public const string ROLE_UPDATED = "RoleUpdated";
    public const string ROLE_DELETED = "RoleDeleted";

    public const string SERVICE_KEY_ISSUED = "ServiceKeyIssued";
    public const string SERVICE_KEY_ROTATED = "ServiceKeyRotated";
    public const string SERVICE_KEY_REVOKED = "ServiceKeyRevoked";
    public const string SERVICE_KEY_PERMISSIONS_CHANGED = "ServiceKeyPermissionsChanged";

    public const string USER_OBJECT = "user";
    public const string ROLE_OBJECT = "role";
    public const string PERMISSION_OBJECT = "permission";
    public const string SERVICE_OBJECT = "service";
}
