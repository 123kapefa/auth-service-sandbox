using System.Text.RegularExpressions;
using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.BrandingSettings;

/// <summary>
/// Название — до 60 символов; цвет — #rrggbb; тема — system, light или dark; логотип — data URL PNG, JPEG, WebP или SVG
/// до 100 КБ. Те же правила проверяет плагин Keycloak.
/// </summary>
public sealed partial class SetBrandingRequestValidator : AbstractValidator<SetBrandingRequest>
{
    public SetBrandingRequestValidator()
    {
        RuleFor(request => request.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithError(GeneralErrors.ValueIsRequired("name"));
        RuleFor(request => request.Name)
            .Must(name => name is null || name.Trim().Length <= BrandingRules.NAME_MAX_LENGTH)
            .WithError(GeneralErrors.LengthIsInvalid("name", max: BrandingRules.NAME_MAX_LENGTH));
        RuleFor(request => request.Color)
            .Must(color => color is null || IsColor(color))
            .WithError(GeneralErrors.InvalidFormat("color"));
        RuleFor(request => request.Theme)
            .Must(theme => theme is not null && BrandingRules.Themes.Contains(theme, StringComparer.Ordinal))
            .WithError(GeneralErrors.ValueIsInvalid("theme"));
        RuleFor(request => request.Logo)
            .Must(logo => logo is null || IsLogo(logo))
            .WithError(GeneralErrors.InvalidFormat("logo"));
        RuleFor(request => request.Logo)
            .Must(logo => logo is null || !IsLogo(logo) || LogoSize(logo) <= BrandingRules.LOGO_MAX_BYTES)
            .WithError(Error.Validation("branding.logo_too_large", "Логотип — не больше 100 КБ.", "logo"));
    }

    internal static bool IsColor(string color) => ColorPattern().IsMatch(color);

    internal static bool IsLogo(string logo) => LogoPattern().IsMatch(logo);

    // Размер картинки по длине base64 (без декодирования): 4 символа — 3 байта, минус «=» в конце.
    internal static long LogoSize(string logo)
    {
        string data = LogoPattern().Match(logo).Groups["data"].Value;
        return (data.Length / 4 * 3L) - data.Count(symbol => symbol == '=');
    }

    // Регистр цвета не важен: сервис сохраняет строчными.
    [GeneratedRegex(BrandingRules.COLOR_PATTERN, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();

    [GeneratedRegex(BrandingRules.LOGO_PATTERN, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex LogoPattern();
}
