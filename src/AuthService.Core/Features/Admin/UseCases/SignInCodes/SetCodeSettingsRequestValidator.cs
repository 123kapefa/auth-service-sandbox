using System.Text.RegularExpressions;
using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.SignInCodes;

/// <summary>
/// Звонки и/или SMS; от 1 до 20 стран (код без «+»); бюджет от 0 до 10 млн ₽; лимиты — в разумных пределах, в сутки не
/// меньше, чем в час. Те же правила проверяет плагин Keycloak.
/// </summary>
public sealed partial class SetCodeSettingsRequestValidator : AbstractValidator<SetCodeSettingsRequest>
{
    public SetCodeSettingsRequestValidator()
    {
        RuleFor(request => request.PhoneChannels)
            .Must(channels => channels is { Count: > 0 })
            .WithError(GeneralErrors.ValueIsRequired("phoneChannels"));
        RuleFor(request => request.PhoneChannels)
            .Must(channels => channels is null || channels.All(channel => CodeSettingsRules.PhoneChannels.Contains(channel, StringComparer.Ordinal)))
            .WithError(GeneralErrors.ValueIsInvalid("phoneChannels"));
        RuleFor(request => request.PhoneChannels)
            .Must(channels => channels is null || channels.Distinct(StringComparer.Ordinal).Count() == channels.Count)
            .WithError(CodeSettingsErrors.Duplicates("phoneChannels"));

        RuleFor(request => request.Countries)
            .Must(countries => countries is { Count: > 0 })
            .WithError(GeneralErrors.ValueIsRequired("countries"));
        RuleFor(request => request.Countries)
            .Must(countries => countries is null || countries.Count <= CodeSettingsRules.COUNTRIES_MAX)
            .WithError(GeneralErrors.OutOfRange("countries", 1, CodeSettingsRules.COUNTRIES_MAX));
        RuleFor(request => request.Countries)
            .Must(countries => countries is null || countries.All(country => country is not null && CountryPattern().IsMatch(country)))
            .WithError(GeneralErrors.InvalidFormat("countries"));
        RuleFor(request => request.Countries)
            .Must(countries => countries is null || countries.Distinct(StringComparer.Ordinal).Count() == countries.Count)
            .WithError(CodeSettingsErrors.Duplicates("countries"));

        RuleFor(request => request.DailyBudget)
            .Must(budget => budget is >= 0 and <= CodeSettingsRules.BUDGET_MAX)
            .WithError(GeneralErrors.OutOfRange("dailyBudget", 0, CodeSettingsRules.BUDGET_MAX));
        Range(request => request.TargetPerHour, "targetPerHour", 1, CodeSettingsRules.TARGET_PER_HOUR_MAX);
        Range(request => request.TargetPerDay, "targetPerDay", 1, CodeSettingsRules.TARGET_PER_DAY_MAX);
        Range(request => request.IpPerHour, "ipPerHour", 1, CodeSettingsRules.IP_PER_HOUR_MAX);
        Range(request => request.RangePerHour, "rangePerHour", 1, CodeSettingsRules.RANGE_PER_HOUR_MAX);
        Range(request => request.CaptchaAfter, "captchaAfter", 0, CodeSettingsRules.CAPTCHA_AFTER_MAX);
        RuleFor(request => request.TargetPerDay)
            .Must((request, day) => day is null || request.TargetPerHour is null || day >= request.TargetPerHour)
            .WithError(CodeSettingsErrors.DayBelowHour());
    }

    private void Range(System.Linq.Expressions.Expression<Func<SetCodeSettingsRequest, int?>> field, string name, int min, int max) =>
        RuleFor(field)
            .Must(value => value is not null && value >= min && value <= max)
            .WithError(GeneralErrors.OutOfRange(name, min, max));

    [GeneratedRegex(CodeSettingsRules.COUNTRY_PATTERN, RegexOptions.CultureInvariant)]
    private static partial Regex CountryPattern();
}
