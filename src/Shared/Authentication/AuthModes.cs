namespace PlatformAuth;

/// <summary>Роли платформы. Они же режимы: сотрудник работает в одном режиме за раз.</summary>
public static class AuthModes
{
    public const string CUSTOMER = "customer";
    public const string MANAGER = "manager";
    public const string COURIER = "courier";
    public const string ADMIN = "admin";
}
