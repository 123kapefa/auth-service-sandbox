using AuthService.Contracts.Admin;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.SetRole;

/// <summary>Роль обязательна; есть ли такая роль у приложения (регистр не важен) — решает хендлер по каталогу.</summary>
public sealed class SetRoleRequestValidator : AbstractValidator<SetRoleRequest>
{
    public SetRoleRequestValidator() =>
        RuleFor(request => request.Role)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("role"));
}
