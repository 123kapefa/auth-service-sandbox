using FluentValidation.Results;
using SharedKernel;

namespace Validation;

public static class ValidationExtensions
{
    /// <summary>Собирает все нарушения валидатора в один Error.Validation.</summary>
    public static Error ToError(this ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        return Error.Validation(validationResult.Errors.SelectMany(ToMessages));
    }

    // Правило с WithError/MustBeValueObject несёт наш Error; правило без него — только текст FluentValidation.
    private static IEnumerable<ErrorMessage> ToMessages(ValidationFailure failure) =>
        failure.CustomState is Error error
            ? error.Messages
            : [new ErrorMessage("value.is.invalid", failure.ErrorMessage, failure.PropertyName)];
}
