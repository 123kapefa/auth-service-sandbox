namespace PlatformAuth;

/// <summary>Имена политик «роль + активный режим» — те же, что в боевом auth-service.</summary>
public static class AuthPolicies
{
    public const string ADMIN_ONLY = "AdminOnly";
    public const string MANAGER_OR_ADMIN = "ManagerOrAdmin";

    /// <summary>Вошёл человек (не сервис по ключу): «я», мои сессии, смена режима.</summary>
    public const string USER = "User";

    /// <summary>Вызывает сервис проекта по своему ключу доступа (токен с client_id).</summary>
    public const string SERVICE = "Service";
}
