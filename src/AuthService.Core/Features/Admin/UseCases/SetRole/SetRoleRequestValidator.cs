using AuthService.Contracts.Admin;
using FluentValidation;
using PlatformAuth;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.SetRole;

/// <summary>Роль обязательна и известна платформе (customer, manager, admin), регистр не важен.</summary>
public sealed class SetRoleRequestValidator : AbstractValidator<SetRoleRequest>
{
    public SetRoleRequestValidator()
    {
        RuleFor(request => request.Role)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("role"));
        RuleFor(request => request.Role)
            .Must(role => SetRoleHandler.Normalize(role) is not null)
            .When(request => !string.IsNullOrWhiteSpace(request.Role))
            .WithError(GeneralErrors.ValueIsInvalid("role"));
    }
}
