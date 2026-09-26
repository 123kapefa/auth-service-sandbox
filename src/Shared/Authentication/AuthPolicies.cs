namespace PlatformAuth;

/// <summary>Имена политик «роль + активный режим» — те же, что в боевом auth-service.</summary>
public static class AuthPolicies
{
    public const string ADMIN_ONLY = "AdminOnly";
    public const string MANAGER_OR_ADMIN = "ManagerOrAdmin";
}
