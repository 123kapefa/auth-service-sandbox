using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Abstractions;

/// <summary>Администрирование пользователей на сервере входа (Keycloak Admin API) глазами сервиса.</summary>
public interface IIdentityAdmin
{
    /// <summary>Пользователь по id; нет такого — NotFound.</summary>
    Task<Result<IdentityUser, Error>> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Поиск пользователей: подстрока в логине, почте, имени (как search Admin API Keycloak), по логину по возрастанию.
    /// search пустой — все. first — сколько пропустить, max — сколько вернуть.
    /// </summary>
    Task<Result<IReadOnlyList<IdentityUser>, Error>> SearchUsersAsync(
        string? search,
        int first,
        int max,
        CancellationToken cancellationToken);

    /// <summary>Пользователь по точному логину: почта (с «@») или логин-телефон цифрами; нет — null.</summary>
    Task<Result<IdentityUser?, Error>> FindUserExactAsync(string login, CancellationToken cancellationToken);

    /// <summary>
    /// Пользователи, которым роль клиента выдана напрямую (без ролей по умолчанию). Нет такого клиента или роли — NotFound.
    /// </summary>
    Task<Result<IReadOnlyList<IdentityUser>, Error>> GetRoleUsersAsync(
        string clientId,
        string roleName,
        int first,
        int max,
        CancellationToken cancellationToken);

    /// <summary>Клиенты, видимые служебной учётке (FGAP v2): clientId и название.</summary>
    Task<Result<IReadOnlyList<ClientInfo>, Error>> GetClientsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Роли пользователя в клиенте (приложении), выданные ему напрямую. Роли по умолчанию realm (customer)
    /// сюда не входят — они есть у всех.
    /// </summary>
    Task<Result<IReadOnlyList<string>, Error>> GetClientRolesAsync(Guid userId, string clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Действующие роли пользователя в клиенте: выданные напрямую, по умолчанию realm (customer) и через составные роли —
    /// вместе с правами в их составе. Нет такого клиента или нет права на него — NotFound.
    /// </summary>
    Task<Result<IReadOnlyList<string>, Error>> GetEffectiveClientRolesAsync(Guid userId, string clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Выдать и снять роли пользователя в клиенте (приложении). Служебной учётке нужно точечное право на этот клиент
    /// (view и map-roles в FGAP v2) — без доступа к остальным клиентам и их секретам.
    /// </summary>
    Task<UnitResult<Error>> SetClientRolesAsync(
        Guid userId,
        string clientId,
        IReadOnlyCollection<string> grant,
        IReadOnlyCollection<string> revoke,
        CancellationToken cancellationToken);

    /// <summary>
    /// Приложения, которыми можно управлять (клиенты с ролью access-denied, видимые служебной учётке),
    /// и открыт ли к ним доступ пользователю.
    /// </summary>
    Task<Result<IReadOnlyList<AppAccessInfo>, Error>> GetAppAccessAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// События входа Keycloak (хранятся realm, eventsExpiration): новые сверху. userId null — всех пользователей;
    /// types — какие (LOGIN, LOGIN_ERROR, LOGOUT…).
    /// </summary>
    Task<Result<IReadOnlyList<LoginEvent>, Error>> GetLoginEventsAsync(
        Guid? userId,
        IReadOnlyCollection<string> types,
        int first,
        int max,
        CancellationToken cancellationToken);

    /// <summary>
    /// Роли клиента приложения с атрибутами: и роли (kind=role), и права (kind=permission). Служебной учётке нужно
    /// точечное право на клиент (FGAP v2). Нет такого клиента или нет права на него — NotFound.
    /// </summary>
    Task<Result<IReadOnlyList<AppRole>, Error>> GetAppRolesAsync(string clientId, CancellationToken cancellationToken);

    /// <summary>Создать роль клиента приложения (право или роль) с описанием и атрибутами. Уже есть — Conflict.</summary>
    Task<UnitResult<Error>> CreateAppRoleAsync(string clientId, AppRole role, CancellationToken cancellationToken);

    /// <summary>Обновить описание и атрибуты роли клиента приложения.</summary>
    Task<UnitResult<Error>> UpdateAppRoleAsync(string clientId, AppRole role, CancellationToken cancellationToken);

    /// <summary>Состав роли приложения: имена ролей (прав), которые в неё входят напрямую.</summary>
    Task<Result<IReadOnlyList<string>, Error>> GetRoleCompositesAsync(string clientId, string roleName, CancellationToken cancellationToken);

    /// <summary>Изменить состав роли: добавить и убрать роли (права) клиента приложения.</summary>
    Task<UnitResult<Error>> SetRoleCompositesAsync(
        string clientId,
        string roleName,
        IReadOnlyCollection<string> add,
        IReadOnlyCollection<string> remove,
        CancellationToken cancellationToken);

    /// <summary>Удалить роль клиента приложения; Keycloak убирает её и из составных ролей.</summary>
    Task<UnitResult<Error>> DeleteAppRoleAsync(string clientId, string name, CancellationToken cancellationToken);

    /// <summary>Типы учётных данных пользователя: password, otp, recovery-authn-codes, webauthn-passwordless…</summary>
    Task<Result<IReadOnlyList<string>, Error>> GetCredentialTypesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Удалить учётные данные этих типов; вернуть, сколько удалено.</summary>
    Task<Result<int, Error>> RemoveCredentialsAsync(Guid userId, IReadOnlyCollection<string> types, CancellationToken cancellationToken);

    /// <summary>
    /// Письмо Keycloak со ссылкой на действия (UPDATE_PASSWORD…): почта realm (SMTP), ссылка живёт lifespan. Нет почты
    /// у пользователя — Conflict.
    /// </summary>
    Task<UnitResult<Error>> SendActionsEmailAsync(
        Guid userId,
        IReadOnlyCollection<string> actions,
        TimeSpan lifespan,
        CancellationToken cancellationToken);

    /// <summary>Задан ли пароль (пользователь мог войти только по коду).</summary>
    Task<Result<bool, Error>> HasPasswordAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Изменить пользователя: включён ли он и атрибуты (null — удалить). Остальное не меняется.</summary>
    Task<UnitResult<Error>> UpdateUserAsync(Guid userId, UserChanges changes, CancellationToken cancellationToken);

    /// <summary>Сессии пользователя на сервере входа: где и когда вошёл, в какие приложения.</summary>
    Task<Result<IReadOnlyList<IdentitySession>, Error>> GetSessionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Завершить одну сессию: её refresh-токены и SSO больше не работают, остальные сессии — да.</summary>
    Task<UnitResult<Error>> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Завершить все сессии пользователя: refresh-токены и SSO больше не работают.</summary>
    Task<UnitResult<Error>> LogoutUserAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Что поменять у пользователя.</summary>
/// <param name="Enabled">Включить или выключить (null — не трогать).</param>
/// <param name="Attributes">Атрибуты: значение — записать, null — удалить.</param>
public sealed record UserChanges(bool? Enabled, IReadOnlyDictionary<string, string?> Attributes);

/// <summary>Пользователь сервера входа.</summary>
/// <param name="Id">Id пользователя (sub в токене).</param>
/// <param name="Username">Логин: номер цифрами или почта.</param>
/// <param name="Email">Почта, если есть.</param>
/// <param name="Phone">Телефон с «+», если есть.</param>
/// <param name="ActiveMode">Активный режим.</param>
/// <param name="Enabled">false — заблокирован.</param>
/// <param name="CreatedAt">Когда создан.</param>
/// <param name="RestrictionReason">Причина блокировки, если указана.</param>
/// <param name="RestrictedAt">Когда заблокирован.</param>
/// <param name="AppModes">Активные режимы в других приложениях: clientId → режим (атрибуты active_mode.{clientId}).</param>
public sealed record IdentityUser(
    Guid Id,
    string Username,
    string? Email,
    string? Phone,
    string? ActiveMode,
    bool Enabled,
    DateTimeOffset CreatedAt,
    string? RestrictionReason = null,
    DateTimeOffset? RestrictedAt = null,
    IReadOnlyDictionary<string, string>? AppModes = null);

/// <summary>Сессия пользователя на сервере входа.</summary>
/// <param name="Id">Id сессии (claim sid в токенах этой сессии).</param>
/// <param name="IpAddress">IP, с которого вошли.</param>
/// <param name="StartedAt">Когда вошли.</param>
/// <param name="LastAccessAt">Последняя активность (вход или refresh).</param>
/// <param name="Apps">Приложения (clientId), в которых открыта сессия.</param>
public sealed record IdentitySession(
    string Id,
    string? IpAddress,
    DateTimeOffset StartedAt,
    DateTimeOffset LastAccessAt,
    IReadOnlyList<string> Apps);

/// <summary>Событие входа на сервере входа.</summary>
/// <param name="Time">Когда.</param>
/// <param name="Type">LOGIN, LOGIN_ERROR, LOGOUT…</param>
/// <param name="UserId">Кто (у неудачного входа с неизвестным логином — null).</param>
/// <param name="IpAddress">С какого IP.</param>
/// <param name="ClientId">В какое приложение.</param>
/// <param name="Error">Причина ошибки (invalid_user_credentials, user_disabled…).</param>
/// <param name="Username">Логин, если Keycloak его записал.</param>
public sealed record LoginEvent(
    DateTimeOffset Time,
    string Type,
    Guid? UserId,
    string? IpAddress,
    string? ClientId,
    string? Error,
    string? Username);

/// <summary>Типы событий входа Keycloak, которые показывает журнал.</summary>
public static class LoginEventTypes
{
    public const string LOGIN = "LOGIN";
    public const string LOGIN_ERROR = "LOGIN_ERROR";
    public const string LOGOUT = "LOGOUT";

    public static IReadOnlyCollection<string> All { get; } = [LOGIN, LOGIN_ERROR, LOGOUT];
}

/// <summary>Роль клиента приложения: роль (режим) или право.</summary>
/// <param name="Id">Id роли в Keycloak (null — ещё не создана).</param>
/// <param name="Name">Имя: manager, users.view…</param>
/// <param name="Description">Описание для админки.</param>
/// <param name="Kind">AppRoleKinds: role или permission.</param>
/// <param name="Source">Откуда право: code (константа в коде сервиса) или admin (создано в админке).</param>
/// <param name="Builtin">Встроенная роль — защищена от удаления.</param>
/// <param name="Version">Версия состава роли (меняется с составом) — в токене mode_version; null — не менялась.</param>
public sealed record AppRole(Guid? Id, string Name, string? Description, string Kind, string? Source, bool Builtin, long? Version = null);

/// <summary>Атрибуты ролей клиента приложения в Keycloak.</summary>
public static class AppRoleKinds
{
    public const string ATTRIBUTE_KIND = "kind";
    public const string ATTRIBUTE_SOURCE = "source";
    public const string ATTRIBUTE_BUILTIN = "builtin";
    public const string ATTRIBUTE_VERSION = "version";

    public const string ROLE = "role";
    public const string PERMISSION = "permission";

    public const string SOURCE_CODE = "code";
    public const string SOURCE_ADMIN = "admin";
}

/// <summary>Типы учётных данных Keycloak и действия из письма.</summary>
public static class Credentials
{
    public const string PASSWORD = "password";
    public const string OTP = "otp";
    public const string RECOVERY_CODES = "recovery-authn-codes";

    /// <summary>Вторые факторы: OTP и коды восстановления (ключ доступа — способ входа, не 2FA).</summary>
    public static IReadOnlyList<string> TwoFactor { get; } = [OTP, RECOVERY_CODES];

    /// <summary>Действие из письма: задать новый пароль.</summary>
    public const string UPDATE_PASSWORD = "UPDATE_PASSWORD";
}

/// <summary>Клиент Keycloak: clientId и название.</summary>
public sealed record ClientInfo(string ClientId, string? Name);

/// <summary>Приложение и доступ к нему пользователя.</summary>
/// <param name="ClientId">Клиент приложения в Keycloak.</param>
/// <param name="Name">Название клиента.</param>
/// <param name="HasAccess">false — доступ закрыт (роль access-denied клиента).</param>
public sealed record AppAccessInfo(string ClientId, string? Name, bool HasAccess);

/// <summary>Роль клиента приложения «доступ закрыт»: кому выдана — тот в это приложение не входит.</summary>
public static class AppRoles
{
    public const string ACCESS_DENIED = "access-denied";
}

/// <summary>Атрибуты пользователя, которые ведёт сервис (кроме phone и active_mode из контракта токена).</summary>
public static class UserAttributes
{
    public const string RESTRICTION_REASON = "restriction_reason";
    public const string RESTRICTED_AT = "restricted_at";

    /// <summary>Версия доступа: меняется со сменой ролей — в токене access_version.</summary>
    public const string ACCESS_VERSION = "access_version";

    /// <summary>Режим в другом приложении — атрибут active_mode.{clientId} (маппер ролей этого приложения, mode.attribute).</summary>
    public const string APP_MODE_PREFIX = "active_mode.";

    public static string AppModeOf(string clientId) => APP_MODE_PREFIX + clientId;
}
