using AuthService.Contracts.Admin;
using AuthService.Core.Authorization;
using AuthService.Domain.ServiceKeys;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.ServiceKeys;

/// <summary>Код — строчная латиница, цифры, дефис, с буквы (из него client_id service-{код}); название — до 100.</summary>
public sealed class IssueServiceKeyRequestValidator : AbstractValidator<IssueServiceKeyRequest>
{
    public const string CODE_PATTERN = "^[a-z][a-z0-9-]*[a-z0-9]$";

    public IssueServiceKeyRequestValidator()
    {
        RuleFor(request => request.Code)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("code"));
        RuleFor(request => request.Code)
            .Matches(CODE_PATTERN)
            .When(request => !string.IsNullOrEmpty(request.Code))
            .WithError(GeneralErrors.InvalidFormat("code"));
        RuleFor(request => request.Code)
            .MaximumLength(ServiceKey.MAX_CODE_LENGTH)
            .WithError(GeneralErrors.LengthIsInvalid("code", max: ServiceKey.MAX_CODE_LENGTH));
        RuleFor(request => request.Name)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("name"));
        RuleFor(request => request.Name)
            .MaximumLength(ServiceKey.MAX_NAME_LENGTH)
            .WithError(GeneralErrors.LengthIsInvalid("name", max: ServiceKey.MAX_NAME_LENGTH));
        RuleForEach(request => request.Permissions)
            .Must(permission => AuthServicePermissions.ServiceAccess.All.Contains(permission, StringComparer.Ordinal))
            .WithError(GeneralErrors.ValueIsInvalid("permissions"));
    }
}

/// <summary>Права сервиса — только из списка прав сервисов (service.*).</summary>
public sealed class SetServicePermissionsRequestValidator : AbstractValidator<SetServicePermissionsRequest>
{
    public SetServicePermissionsRequestValidator() =>
        RuleForEach(request => request.Permissions)
            .Must(permission => AuthServicePermissions.ServiceAccess.All.Contains(permission, StringComparer.Ordinal))
            .WithError(GeneralErrors.ValueIsInvalid("permissions"));
}
