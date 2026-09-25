using SharedKernel;
using Xunit;

namespace Shared.UnitTests;

public sealed class GeneralErrorsTests
{
    [Fact]
    public void Unauthorized_IsAuthenticationError() =>
        Assert.Equal(ErrorType.AUTHENTICATION, GeneralErrors.Unauthorized().Type);

    [Fact]
    public void Forbidden_IsAuthorizationError() =>
        Assert.Equal(ErrorType.AUTHORIZATION, GeneralErrors.Forbidden().Type);

    [Fact]
    public void ValueIsRequired_FillsInvalidField()
    {
        ErrorMessage message = Assert.Single(GeneralErrors.ValueIsRequired("Email").Messages);

        Assert.Equal("Email", message.InvalidField);
    }
}
