using AuthService.Contracts.Admin;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.RoleCatalog;

/// <summary>Имя роли — строчная латиница, цифры, дефис (без точек: с точкой — права); описание до 255.</summary>
public sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public const string NAME_PATTERN = "^[a-z][a-z0-9-]{1,49}$";

    public CreateRoleRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("name"));
        RuleFor(request => request.Name)
            .Matches(NAME_PATTERN)
            .When(request => !string.IsNullOrEmpty(request.Name))
            .WithError(GeneralErrors.InvalidFormat("name"));
        RuleFor(request => request.Description)
            .MaximumLength(255)
            .WithError(GeneralErrors.LengthIsInvalid("description", max: 255));
    }
}

/// <summary>Состав прав обязателен (пустой список — роль без прав); описание до 255.</summary>
public sealed class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(request => request.Permissions)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired("permissions"));
        RuleFor(request => request.Description)
            .MaximumLength(255)
            .WithError(GeneralErrors.LengthIsInvalid("description", max: 255));
    }
}
