namespace AuthService.Contracts.Admin;

/// <summary>
/// Коды подтверждения проекта — GET /auth/admin/codes/settings: что меняет админ и (только для показа) настройки
/// сервера. Деньги — в копейках.
/// </summary>
/// <param name="PhoneChannels">Как слать код на телефон: call (звонок-сброс, первым) и/или sms (запасной канал).</param>
/// <param name="Countries">Коды стран, на номера которых шлём код, без «+»: 7, 375.</param>
/// <param name="DailyBudget">Дневной бюджет на звонки и SMS, копейки; 0 — без бюджета. Кончился — SMS до полуночи не уходят.</param>
/// <param name="TargetPerHour">Отправок на один номер (почту) за час.</param>
/// <param name="TargetPerDay">Отправок на один номер (почту) за сутки.</param>
/// <param name="IpPerHour">Отправок с одного IP за час.</param>
/// <param name="RangePerHour">Отправок на диапазон номеров (первые RangeDigits цифр) за час.</param>
/// <param name="CaptchaAfter">После стольких отправок за час (с IP или на номер) — капча.</param>
/// <param name="RangeDigits">Диапазон номеров — первые столько цифр (настройка сервера).</param>
/// <param name="SmsCost">Цена SMS, копейки (настройка сервера, из договора с провайдером).</param>
/// <param name="CallCost">Цена звонка, копейки (настройка сервера).</param>
/// <param name="BudgetAlertPercent">При такой доле бюджета — письмо админам (настройка сервера).</param>
/// <param name="Zone">Часовой пояс, в котором начинаются сутки бюджета (настройка сервера).</param>
public sealed record CodeSettingsResponse(
    IReadOnlyList<string> PhoneChannels,
    IReadOnlyList<string> Countries,
    long DailyBudget,
    int TargetPerHour,
    int TargetPerDay,
    int IpPerHour,
    int RangePerHour,
    int CaptchaAfter,
    int RangeDigits,
    long SmsCost,
    long CallCost,
    int BudgetAlertPercent,
    string Zone);

/// <summary>PUT /auth/admin/codes/settings — сохранить всё, что меняет админ, целиком (как отдал GET).</summary>
public sealed record SetCodeSettingsRequest(
    IReadOnlyList<string>? PhoneChannels,
    IReadOnlyList<string>? Countries,
    long? DailyBudget,
    int? TargetPerHour,
    int? TargetPerDay,
    int? IpPerHour,
    int? RangePerHour,
    int? CaptchaAfter);

/// <summary>
/// Расход кодов за сегодня — GET /auth/admin/codes/usage: сутки начинаются в полночь по часовому поясу бюджета.
/// </summary>
/// <param name="Date">Сутки, yyyy-MM-dd.</param>
/// <param name="Zone">Часовой пояс суток.</param>
/// <param name="Budget">Бюджет: израсходовано и сколько всего.</param>
/// <param name="Call">Звонки: сколько и во что обошлись.</param>
/// <param name="Sms">SMS: сколько и во что обошлись.</param>
/// <param name="Email">Письма: сколько (бесплатно).</param>
/// <param name="Sent">Всего кодов отправлено.</param>
/// <param name="Verified">Сколько кодов введено верно.</param>
/// <param name="VerifiedPercent">Доля введённых, %; null — сегодня ничего не отправлено.</param>
public sealed record CodeUsageResponse(
    string Date,
    string Zone,
    CodeBudgetUsage Budget,
    CodeChannelUsage Call,
    CodeChannelUsage Sms,
    CodeChannelUsage Email,
    long Sent,
    long Verified,
    int? VerifiedPercent);

/// <summary>Бюджет за сутки, копейки.</summary>
/// <param name="Spent">Израсходовано.</param>
/// <param name="Limit">Дневной бюджет; 0 — без бюджета.</param>
/// <param name="AlertPercent">При такой доле — письмо админам.</param>
/// <param name="Exhausted">Кончился: SMS до полуночи не уходят (звонки и почта — да).</param>
public sealed record CodeBudgetUsage(long Spent, long Limit, int AlertPercent, bool Exhausted);

/// <summary>Отправлено способом за сутки и во что обошлось, копейки.</summary>
public sealed record CodeChannelUsage(long Sent, long Cost);
