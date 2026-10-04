using AuthService.Contracts.Auth;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Auth.UseCases.Callback;

/// <summary>Без ошибки от сервера входа обязательны code и state.</summary>
public sealed class CallbackRequestValidator : AbstractValidator<CallbackRequest>
{
    public CallbackRequestValidator() =>
        When(request => string.IsNullOrEmpty(request.Error), () =>
        {
            RuleFor(request => request.Code)
                .NotEmpty()
                .WithError(GeneralErrors.ValueIsRequired("code"));
            RuleFor(request => request.State)
                .NotEmpty()
                .WithError(GeneralErrors.ValueIsRequired("state"));
        });
}
