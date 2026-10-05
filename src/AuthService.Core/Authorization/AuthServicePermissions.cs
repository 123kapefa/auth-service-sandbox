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

        /// <summary>Сброс пароля (письмо со ссылкой) и отключение 2FA — уровень аккаунта, только админ проекта.</summary>
        public const string MANAGE_CREDENTIALS = "users.credentials.manage";
    }

    public static class Audit
    {
        public const string VIEW = "audit.view";
    }

    public static class ProjectServices
    {
        /// <summary>Сервисы проекта и их ключи доступа: выдать, перевыпустить, отозвать — только админ проекта.</summary>
        public const string MANAGE = "services.manage";
    }

    public static class Branding
    {
        /// <summary>Оформление проекта: название, логотип, цвет и тема страниц входа — только админ проекта.</summary>
        public const string MANAGE = "branding.manage";
    }

    /// <summary>
    /// Права сервисов проекта (по ключу доступа) в сервисе входа. Выдаются ключу при выдаче или позже; людям — не нужны:
    /// эндпоинты с ними пускают только сервисы (RequireService).
    /// </summary>
    public static class ServiceAccess
    {
        /// <summary>Читать пользователя по Id или логину: телефон, почта, заблокирован ли.</summary>
        public const string USERS_READ = "service.users.read";

        /// <summary>Проверить, есть ли у пользователя право в приложении (фоновые задачи без токена пользователя).</summary>
        public const string ACCESS_CHECK = "service.access.check";

        /// <summary>
        /// Объявить каталог своего приложения: права и встроенные роли из кода (PUT /auth/services/catalog). Только
        /// для приложения с тем же кодом, что у ключа: ключ «orders» (service-orders) → приложение orders.
        /// </summary>
        public const string CATALOG_DECLARE = "service.catalog.declare";

        public static IReadOnlyList<string> All { get; } = [USERS_READ, ACCESS_CHECK, CATALOG_DECLARE];
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
        new(Users.MANAGE_CREDENTIALS, "Сброс пароля и 2FA"),
        new(Audit.VIEW, "Журнал действий и входов"),
        new(Roles.MANAGE, "Роли и права приложения"),
        new(ProjectServices.MANAGE, "Сервисы проекта и их ключи доступа"),
        new(Branding.MANAGE, "Оформление проекта: название, логотип, цвет, тема"),
        new(ServiceAccess.USERS_READ, "Сервис: читать пользователя"),
        new(ServiceAccess.ACCESS_CHECK, "Сервис: проверить доступ пользователя"),
        new(ServiceAccess.CATALOG_DECLARE, "Сервис: объявить права и роли своего приложения"),
    ];
}
