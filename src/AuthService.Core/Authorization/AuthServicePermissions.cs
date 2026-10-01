using PlatformAuth;

namespace AuthService.Core.Authorization;

/// <summary>
/// Права сервиса. В коде — только эти константы, строки прав не пишутся руками. Catalog — они же с описаниями:
/// при старте сервис заводит недостающие в Keycloak (системные права, source=code).
/// </summary>
public static class AuthServicePermissions
{
    public static class Profiles
    {
        public const string VIEW = "profile.view";
    }

    public static class Users
    {
        public const string VIEW = "users.view";
        public const string BLOCK = "users.block";
        public const string MANAGE_ROLES = "users.roles.manage";

        /// <summary>Завершать сессии пользователя («выйти везде») — уровень аккаунта, только админ проекта.</summary>
        public const string MANAGE_SESSIONS = "users.sessions.manage";
    }

    public static class Audit
    {
        public const string VIEW = "audit.view";
    }

    public static class Roles
    {
        /// <summary>Роли и права приложения: создавать, менять состав, удалять.</summary>
        public const string MANAGE = "roles.manage";
    }

    public static IReadOnlyList<PermissionDefinition> Catalog { get; } =
    [
        new(Profiles.VIEW, "Свой профиль"),
        new(Users.VIEW, "Пользователи: список и карточка"),
        new(Users.BLOCK, "Блокировка аккаунта и доступ к приложению"),
        new(Users.MANAGE_ROLES, "Смена ролей пользователей"),
        new(Users.MANAGE_SESSIONS, "«Выйти везде» за пользователя"),
        new(Audit.VIEW, "Журнал действий и входов"),
        new(Roles.MANAGE, "Роли и права приложения"),
    ];
}
