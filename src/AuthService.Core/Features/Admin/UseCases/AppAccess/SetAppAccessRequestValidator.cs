using AuthService.Contracts.Admin;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.AppAccess;

/// <summary>hasAccess обязателен.</summary>
public sealed class SetAppAccessRequestValidator : AbstractValidator<SetAppAccessRequest>
{
    public SetAppAccessRequestValidator() =>
        RuleFor(request => request.HasAccess)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired("hasAccess"));
}
