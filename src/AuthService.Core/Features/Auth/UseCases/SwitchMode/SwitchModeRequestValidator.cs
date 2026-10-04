using AuthService.Contracts.Auth;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Auth.UseCases.SwitchMode;

/// <summary>Режим обязателен; известен ли он и доступен ли пользователю — решает хендлер (403, как в боевом сервисе).</summary>
public sealed class SwitchModeRequestValidator : AbstractValidator<SwitchModeRequest>
{
    public SwitchModeRequestValidator() =>
        RuleFor(request => request.ActiveMode)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("activeMode"));
}
