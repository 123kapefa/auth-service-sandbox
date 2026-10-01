namespace PlatformAuth;

/// <summary>Роли платформы. Они же режимы: сотрудник работает в одном режиме за раз.</summary>
public static class AuthModes
{
    public const string CUSTOMER = "customer";
    public const string MANAGER = "manager";
    public const string ADMIN = "admin";

    /// <summary>Все режимы платформы; customer доступен всем.</summary>
    public static IReadOnlyList<string> All { get; } = [CUSTOMER, MANAGER, ADMIN];
}
