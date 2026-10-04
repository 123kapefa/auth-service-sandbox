using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;
using Validation;
using Xunit;

namespace Shared.UnitTests;

public sealed class ValidationTests
{
    private readonly TestRequestValidator _validator = new();

    [Fact]
    public void ToError_RuleWithError_ReturnsAttachedError()
    {
        Error error = Validate(new TestRequest(string.Empty, "+79990000001", "Name"));

        ErrorMessage message = Assert.Single(error.Messages);
        Assert.Equal("value.is.required", message.Code);
        Assert.Equal(nameof(TestRequest.Email), message.InvalidField);
    }

    [Fact]
    public void ToError_MustBeValueObject_ReturnsFactoryError()
    {
        Error error = Validate(new TestRequest("user@example.test", "79990000001", "Name"));

        ErrorMessage message = Assert.Single(error.Messages);
        Assert.Equal("invalid.format", message.Code);
        Assert.Equal(nameof(TestRequest.Phone), message.InvalidField);
    }

    [Fact]
    public void ToError_RuleWithoutError_FallsBackToValueIsInvalid()
    {
        Error error = Validate(new TestRequest("user@example.test", "+79990000001", string.Empty));

        ErrorMessage message = Assert.Single(error.Messages);
        Assert.Equal("value.is.invalid", message.Code);
        Assert.Equal(nameof(TestRequest.Name), message.InvalidField);
    }

    [Fact]
    public void ToError_SeveralViolations_CollectedIntoOneValidationError()
    {
        Error error = Validate(new TestRequest(string.Empty, "bad", string.Empty));

        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal(["value.is.required", "invalid.format", "value.is.invalid"], error.Messages.Select(m => m.Code));
    }

    private Error Validate(TestRequest request)
    {
        ValidationResult result = _validator.Validate(request);
        Assert.False(result.IsValid);
        return result.ToError();
    }

    private sealed record TestRequest(string Email, string Phone, string Name);

    private sealed class TestRequestValidator : AbstractValidator<TestRequest>
    {
        public TestRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .WithError(GeneralErrors.ValueIsRequired(nameof(TestRequest.Email)));

            RuleFor(x => x.Phone)
                .MustBeValueObject(phone => phone.StartsWith('+')
                    ? Result.Success<string, Error>(phone)
                    : GeneralErrors.InvalidFormat(nameof(TestRequest.Phone)));

            RuleFor(x => x.Name).NotEmpty();
        }
    }
}
