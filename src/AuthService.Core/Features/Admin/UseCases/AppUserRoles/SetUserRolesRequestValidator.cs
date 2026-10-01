using AuthService.Contracts.Admin;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.AppUserRoles;

/// <summary>Список ролей обязателен (пустой — только роли по умолчанию), имена непустые; есть ли такие роли — решает хендлер.</summary>
public sealed class SetUserRolesRequestValidator : AbstractValidator<SetUserRolesRequest>
{
    public SetUserRolesRequestValidator()
    {
        RuleFor(request => request.Roles)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired("roles"));
        RuleFor(request => request.Roles)
            .Must(roles => roles!.All(role => !string.IsNullOrWhiteSpace(role)))
            .When(request => request.Roles is not null)
            .WithError(GeneralErrors.ValueIsInvalid("roles"));
    }
}
