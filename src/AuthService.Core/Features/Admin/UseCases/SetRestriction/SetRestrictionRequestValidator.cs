using AuthService.Contracts.Admin;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.SetRestriction;

/// <summary>restricted обязателен; причина — до 500 символов, как в боевом сервисе.</summary>
public sealed class SetRestrictionRequestValidator : AbstractValidator<SetRestrictionRequest>
{
    public const int MAX_REASON_LENGTH = 500;

    public SetRestrictionRequestValidator()
    {
        RuleFor(request => request.Restricted)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired("restricted"));
        RuleFor(request => request.Reason)
            .MaximumLength(MAX_REASON_LENGTH)
            .WithError(GeneralErrors.LengthIsInvalid("reason", max: MAX_REASON_LENGTH));
    }
}
