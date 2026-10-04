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

    /// <summary>Права активного режима (массив): кладёт сервер входа, по ним RequirePermissions.</summary>
    public const string PERMISSIONS = "permissions";

    /// <summary>Приложение (клиент), которому выдан токен.</summary>
    public const string AUTHORIZED_PARTY = "azp";

    /// <summary>Когда выдан токен (секунды Unix).</summary>
    public const string ISSUED_AT = "iat";

    /// <summary>Id сессии сервера входа: какая из сессий пользователя — текущая.</summary>
    public const string SESSION_ID = "sid";

    /// <summary>Версия доступа пользователя: меняется, когда ему выдали или сняли роль (умный рефреш).</summary>
    public const string ACCESS_VERSION = "access_version";

    /// <summary>Версия роли активного режима: меняется, когда меняется состав роли (умный рефреш).</summary>
    public const string MODE_VERSION = "mode_version";

    /// <summary>Когда пользователь вошёл (секунды Unix): для действий, требующих недавнего входа.</summary>
    public const string AUTH_TIME = "auth_time";

    /// <summary>
    /// Сервис проекта, получивший токен своим ключом доступа (client_credentials): его client_id. Есть только у токенов
    /// сервисов — по нему сервис отличается от человека.
    /// </summary>
    public const string CLIENT_ID = "client_id";
}
