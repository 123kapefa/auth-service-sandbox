using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Abstractions;

/// <summary>
/// Способы входа проекта на сервере входа (плагин Keycloak platform-sign-in): что вообще доступно людям проекта, 2FA,
/// переходный период. Плагин применяет их при каждом входе; ключ доступа, Яндекс ID и Госуслуги он выключает
/// переключателями самого Keycloak.
/// </summary>
public interface IProjectSignIn
{
    Task<Result<ProjectSignInSettings, Error>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Сохранить; возвращает то, что действует теперь.</summary>
    Task<Result<ProjectSignInSettings, Error>> SetAsync(ProjectSignInSettings settings, CancellationToken cancellationToken);

    /// <summary>Сколько людей входит способом и сколько — только им.</summary>
    Task<Result<SignInMethodUsers, Error>> GetAffectedAsync(string method, CancellationToken cancellationToken);
}

/// <summary>Способы входа проекта: способ (ProjectSignInRules.Methods) → состояние; 2FA обязательна.</summary>
public sealed record ProjectSignInSettings(IReadOnlyDictionary<string, ProjectSignInMethod> Methods, bool TwoFactorRequired);

/// <summary>Состояние способа.</summary>
/// <param name="Status">ProjectSignInRules.ON, OFF или TRANSITION.</param>
/// <param name="Until">Конец переходного периода (включительно).</param>
/// <param name="Since">Начало переходного периода (ставит плагин).</param>
/// <param name="Configured">Внешний вход настроен в Keycloak (null — не внешний).</param>
public sealed record ProjectSignInMethod(string Status, DateOnly? Until = null, DateTimeOffset? Since = null, bool? Configured = null);

public sealed record SignInMethodUsers(string Method, long Users, long Only);

/// <summary>Правила способов входа проекта — те же, что проверяет плагин Keycloak (ProjectSignIn).</summary>
public static class ProjectSignInRules
{
    public const string PHONE = "phone";
    public const string EMAIL = "email";
    public const string PASSWORD = "password";
    public const string PASSKEY = "passkey";
    public const string YANDEX = "yandex";
    public const string ESIA = "esia";

    public const string ON = "on";
    public const string OFF = "off";
    public const string TRANSITION = "transition";

    public const string TWO_FACTOR_OPTIONAL = "optional";
    public const string TWO_FACTOR_REQUIRED = "required";

    public static IReadOnlyList<string> Methods { get; } = [PHONE, EMAIL, PASSWORD, PASSKEY, YANDEX, ESIA];

    public static IReadOnlyList<string> Statuses { get; } = [ON, OFF, TRANSITION];

    /// <summary>Способы, которыми регистрируются: хотя бы один включён всегда.</summary>
    public static IReadOnlyList<string> Registration { get; } = [PHONE, EMAIL, YANDEX, ESIA];

    /// <summary>Способы с переходным периодом; ключ доступа и внешний вход выключаются сразу.</summary>
    public static IReadOnlyList<string> Transitional { get; } = [PHONE, EMAIL, PASSWORD];

    /// <summary>Переходный период — не дольше года.</summary>
    public const int TRANSITION_MAX_DAYS = 366;

    /// <summary>Сутки переходного периода — по Москве, как у плагина (UTC+3, без перехода на летнее время).</summary>
    public static TimeSpan MoscowOffset { get; } = TimeSpan.FromHours(3);

    public static DateOnly Today(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return DateOnly.FromDateTime(time.GetUtcNow().ToOffset(MoscowOffset).DateTime);
    }
}

public static class ProjectSignInErrors
{
    /// <summary>Плагин ответил 401/403: у служебной учётки нет роли manage-sign-in.</summary>
    public static Error NotPermitted() =>
        Error.Failure("sign_in.not_permitted", "Сервису входа не выдано право на способы входа проекта (роль manage-sign-in в Keycloak).");

    /// <summary>Плагин ответил 404 без тела: в Keycloak нет плагина platform-sign-in (старый образ).</summary>
    public static Error NotInstalled() =>
        Error.Failure("sign_in.not_installed", "Способы входа проекта недоступны: в Keycloak нет плагина platform-sign-in.");

    /// <summary>Плагин не принял настройки (например, внешний вход без ключей).</summary>
    public static Error Rejected(string message) =>
        Error.Validation("sign_in.invalid", message);

    public static Error MethodNotFound(string method) =>
        Error.NotFound("sign_in.method_not_found", $"Нет такого способа входа: {method}.");

    public static Error NoRegistration() =>
        Error.Validation("sign_in.no_registration", "Нужен хотя бы один способ регистрации: телефон, почта, Яндекс ID или Госуслуги.", "methods");

    public static Error TransitionNotAllowed(string method) =>
        Error.Validation("sign_in.transition_not_allowed", "Переходный период — только у входа по телефону, по почте и пароля.", $"methods.{method}");

    public static Error TransitionDate(string method) =>
        Error.Validation("sign_in.transition_date", "Переходный период — до даты в будущем, не дальше года.", $"methods.{method}");
}
