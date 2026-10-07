using System.Globalization;
using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.SignInCodes;

/// <summary>
/// Все шесть способов — on, off или transition (переходный — только телефон, почта, пароль; до даты в будущем, не
/// дальше года); хотя бы один способ регистрации включён; 2FA — optional или required. Те же правила проверяет плагин.
/// </summary>
public sealed class SetSignInMethodsRequestValidator : AbstractValidator<SetSignInMethodsRequest>
{
    public SetSignInMethodsRequestValidator(TimeProvider time)
    {
        RuleFor(request => request.Methods)
            .Must(methods => methods is not null && ProjectSignInRules.Methods.All(methods.ContainsKey) && methods.Count == ProjectSignInRules.Methods.Count)
            .WithError(GeneralErrors.ValueIsRequired("methods"));
        RuleFor(request => request.Methods)
            .Must(methods => methods is null || methods.Values.All(state => state?.Status is not null && ProjectSignInRules.Statuses.Contains(state.Status, StringComparer.Ordinal)))
            .WithError(GeneralErrors.ValueIsInvalid("methods"));
        RuleFor(request => request.TwoFactor)
            .Must(twoFactor => twoFactor is ProjectSignInRules.TWO_FACTOR_OPTIONAL or ProjectSignInRules.TWO_FACTOR_REQUIRED)
            .WithError(GeneralErrors.ValueIsInvalid("twoFactor"));

        RuleFor(request => request.Methods)
            .Custom((methods, context) =>
            {
                if (methods is null)
                {
                    return;
                }

                DateOnly today = ProjectSignInRules.Today(time);
                foreach ((string method, SetSignInMethodState? state) in methods)
                {
                    if (!string.Equals(state?.Status, ProjectSignInRules.TRANSITION, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!ProjectSignInRules.Transitional.Contains(method, StringComparer.Ordinal))
                    {
                        AddError(context, ProjectSignInErrors.TransitionNotAllowed(method));
                    }
                    else if (!DateOnly.TryParseExact(state!.Until, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly until)
                        || until <= today || until > today.AddDays(ProjectSignInRules.TRANSITION_MAX_DAYS))
                    {
                        AddError(context, ProjectSignInErrors.TransitionDate(method));
                    }
                }

                if (!ProjectSignInRules.Registration.Any(method => methods.TryGetValue(method, out SetSignInMethodState? state) && string.Equals(state?.Status, ProjectSignInRules.ON, StringComparison.Ordinal)))
                {
                    AddError(context, ProjectSignInErrors.NoRegistration());
                }
            });
    }

    private static void AddError(ValidationContext<SetSignInMethodsRequest> context, Error error) =>
        context.AddFailure(new ValidationFailure(context.PropertyPath, error.GetMessage()) { CustomState = error });
}
