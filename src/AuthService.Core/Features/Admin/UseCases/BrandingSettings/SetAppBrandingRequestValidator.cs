using System.Text.RegularExpressions;
using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.BrandingSettings;

/// <summary>
/// Оформление приложения: всё необязательно (null — как у проекта), заданное — по правилам проекта: название до 60
/// символов (не пустое), цвет #rrggbb, логотип — data URL PNG, JPEG, WebP или SVG до 100 КБ.
/// </summary>
public sealed partial class SetAppBrandingRequestValidator : AbstractValidator<SetAppBrandingRequest>
{
    public SetAppBrandingRequestValidator()
    {
        RuleFor(request => request.Name)
            .Must(name => name is null || (!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= BrandingRules.NAME_MAX_LENGTH))
            .WithError(GeneralErrors.LengthIsInvalid("name", min: 1, max: BrandingRules.NAME_MAX_LENGTH));
        RuleFor(request => request.Color)
            .Must(color => color is null || SetBrandingRequestValidator.IsColor(color))
            .WithError(GeneralErrors.InvalidFormat("color"));
        RuleFor(request => request.Logo)
            .Must(logo => logo is null || SetBrandingRequestValidator.IsLogo(logo))
            .WithError(GeneralErrors.InvalidFormat("logo"));
        RuleFor(request => request.Logo)
            .Must(logo => logo is null || !SetBrandingRequestValidator.IsLogo(logo) || SetBrandingRequestValidator.LogoSize(logo) <= BrandingRules.LOGO_MAX_BYTES)
            .WithError(Error.Validation("branding.logo_too_large", "Логотип — не больше 100 КБ.", "logo"));
    }
}
