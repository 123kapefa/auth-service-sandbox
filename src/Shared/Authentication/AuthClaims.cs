namespace PlatformAuth;

/// <summary>Имена claims в access-токене: контракт между сервером входа и сервисами.</summary>
public static class AuthClaims
{
    public const string SUBJECT = "sub";

    /// <summary>Роли учётной записи (массив).</summary>
    public const string ROLE = "role";

    /// <summary>Режимы, в которые пользователь может переключиться.</summary>
    public const string AVAILABLE_ROLES = "available_roles";

    /// <summary>Режим, в котором пользователь работает сейчас: по нему проверяется доступ.</summary>
    public const string ACTIVE_MODE = "active_mode";

    public const string PHONE = "phone";
}
