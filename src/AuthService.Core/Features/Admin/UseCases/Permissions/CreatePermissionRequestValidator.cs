using AuthService.Contracts.Admin;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.Permissions;

/// <summary>Имя — как у прав в коде: части через точку, строчная латиница и цифры (reports.export); описание — до 255.</summary>
public sealed class CreatePermissionRequestValidator : AbstractValidator<CreatePermissionRequest>
{
    public const string NAME_PATTERN = @"^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9_-]*)+$";

    public CreatePermissionRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("name"));
        RuleFor(request => request.Name)
            .Matches(NAME_PATTERN)
            .When(request => !string.IsNullOrEmpty(request.Name))
            .WithError(GeneralErrors.InvalidFormat("name"));
        RuleFor(request => request.Name)
            .MaximumLength(100)
            .WithError(GeneralErrors.LengthIsInvalid("name", max: 100));
        RuleFor(request => request.Description)
            .MaximumLength(255)
            .WithError(GeneralErrors.LengthIsInvalid("description", max: 255));
    }
}
