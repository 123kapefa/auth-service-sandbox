using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Abstractions;

/// <summary>
/// Коды подтверждения проекта на сервере входа (плагин Keycloak platform-codes): что меняет админ — способы отправки
/// на телефон, страны, бюджет, лимиты защиты от накрутки — и расход за сегодня. Хранится в атрибутах realm; плагин
/// применяет со следующей отправки кода.
/// </summary>
public interface IProjectCodes
{
    Task<Result<ProjectCodes, Error>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Сохранить; возвращает настройки, по которым коды отправляются теперь.</summary>
    Task<Result<ProjectCodes, Error>> SetAsync(ProjectCodeSettings settings, CancellationToken cancellationToken);

    Task<Result<ProjectCodeUsage, Error>> GetUsageAsync(CancellationToken cancellationToken);
}

/// <summary>Настройки кодов: меняемые и настройки сервера (их админ не меняет).</summary>
public sealed record ProjectCodes(ProjectCodeSettings Settings, ProjectCodeServerSettings Server);

/// <summary>Что меняет админ. Деньги — копейки.</summary>
/// <param name="PhoneChannels">CodeSettingsRules.CALL и/или CodeSettingsRules.SMS.</param>
/// <param name="Countries">Коды стран без «+».</param>
/// <param name="DailyBudget">Дневной бюджет; 0 — без бюджета.</param>
/// <param name="TargetPerHour">Отправок на номер за час.</param>
/// <param name="TargetPerDay">Отправок на номер за сутки.</param>
/// <param name="IpPerHour">Отправок с IP за час.</param>
/// <param name="RangePerHour">Отправок на диапазон номеров за час.</param>
/// <param name="CaptchaAfter">После стольких отправок за час — капча.</param>
public sealed record ProjectCodeSettings(
    IReadOnlyList<string> PhoneChannels,
    IReadOnlyList<string> Countries,
    long DailyBudget,
    int TargetPerHour,
    int TargetPerDay,
    int IpPerHour,
    int RangePerHour,
    int CaptchaAfter);

/// <summary>Настройки сервера: цены (копейки), диапазон номеров, порог оповещения, часовой пояс суток.</summary>
public sealed record ProjectCodeServerSettings(int RangeDigits, long SmsCost, long CallCost, int BudgetAlertPercent, string Zone);

/// <summary>Расход за сутки (копейки).</summary>
public sealed record ProjectCodeUsage(
    DateOnly Date,
    string Zone,
    long Spent,
    long Budget,
    int AlertPercent,
    bool Exhausted,
    CodeChannelCount Call,
    CodeChannelCount Sms,
    CodeChannelCount Email,
    long Sent,
    long Verified,
    int? VerifiedPercent);

public sealed record CodeChannelCount(long Sent, long Cost);

/// <summary>Ограничения настроек кодов — те же, что проверяет плагин Keycloak (CodeSettings).</summary>
public static class CodeSettingsRules
{
    public const string CALL = "call";
    public const string SMS = "sms";

    public static IReadOnlyList<string> PhoneChannels { get; } = [CALL, SMS];

    public const int COUNTRIES_MAX = 20;

    /// <summary>Код страны без «+»: 1–4 цифры, не с нуля.</summary>
    public const string COUNTRY_PATTERN = "^[1-9][0-9]{0,3}$";

    /// <summary>10 млн ₽ в сутки, копейки.</summary>
    public const long BUDGET_MAX = 1_000_000_000L;

    public const int TARGET_PER_HOUR_MAX = 100;
    public const int TARGET_PER_DAY_MAX = 1000;
    public const int IP_PER_HOUR_MAX = 10_000;
    public const int RANGE_PER_HOUR_MAX = 100_000;
    public const int CAPTCHA_AFTER_MAX = 100;
}

public static class CodeSettingsErrors
{
    /// <summary>Плагин ответил 401/403: у служебной учётки нет роли manage-sign-in.</summary>
    public static Error NotPermitted() =>
        Error.Failure("codes.not_permitted", "Сервису входа не выдано право на настройки кодов (роль manage-sign-in в Keycloak).");

    /// <summary>Плагин ответил 404: в Keycloak нет плагина platform-codes (старый образ).</summary>
    public static Error NotInstalled() =>
        Error.Failure("codes.not_installed", "Настройки кодов недоступны: в Keycloak нет плагина platform-codes.");

    /// <summary>Плагин не принял значения (обычно их уже отсёк сервис).</summary>
    public static Error Rejected(string message) =>
        Error.Validation("codes.invalid", message);

    public static Error DayBelowHour() =>
        Error.Validation("codes.day_below_hour", "На номер в сутки — не меньше, чем в час.", "targetPerDay");

    public static Error Duplicates(string field) =>
        Error.Validation("codes.duplicates", "Значения повторяются.", field);
}
