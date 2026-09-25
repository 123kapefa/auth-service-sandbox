using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;

namespace Validation;

public static class CustomValidators
{
    /// <summary>Привязывает к правилу наш Error: при нарушении ToError вернёт именно его.</summary>
    public static IRuleBuilderOptions<T, TProperty> WithError<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(error);

        return rule
            .WithState(_ => error)
            .WithMessage(error.GetMessage());
    }

    /// <summary>Проверяет значение фабрикой value object (например, Email.Of): ошибка фабрики становится ошибкой валидации.</summary>
    public static IRuleBuilderOptionsConditions<T, TProperty> MustBeValueObject<T, TProperty, TValueObject>(
        this IRuleBuilder<T, TProperty> ruleBuilder,
        Func<TProperty, Result<TValueObject, Error>> factoryMethod)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);
        ArgumentNullException.ThrowIfNull(factoryMethod);

        return ruleBuilder.Custom((value, context) =>
        {
            Result<TValueObject, Error> result = factoryMethod(value);
            if (result.IsSuccess)
            {
                return;
            }

            context.AddFailure(new ValidationFailure(context.PropertyPath, result.Error.GetMessage())
            {
                CustomState = result.Error,
            });
        });
    }
}
