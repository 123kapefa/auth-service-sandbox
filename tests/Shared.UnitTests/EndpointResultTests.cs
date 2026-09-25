using System.Text.Json;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Http;
using SharedKernel;
using Xunit;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Shared.UnitTests;

public sealed class EndpointResultTests
{
    public static TheoryData<ErrorType, int> StatusCodeByErrorType => new()
    {
        { ErrorType.VALIDATION, StatusCodes.Status400BadRequest },
        { ErrorType.NOT_FOUND, StatusCodes.Status404NotFound },
        { ErrorType.CONFLICT, StatusCodes.Status409Conflict },
        { ErrorType.AUTHENTICATION, StatusCodes.Status401Unauthorized },
        { ErrorType.AUTHORIZATION, StatusCodes.Status403Forbidden },
        { ErrorType.RATE_LIMIT, StatusCodes.Status429TooManyRequests },
        { ErrorType.FAILURE, StatusCodes.Status500InternalServerError },
    };

    [Theory]
    [MemberData(nameof(StatusCodeByErrorType))]
    public async Task Failure_MapsErrorTypeToStatusCode(ErrorType errorType, int expectedStatusCode)
    {
        EndpointResult<int> result = Result.Failure<int, Error>(CreateError(errorType));

        (int statusCode, JsonElement body) = await ExecuteAsync(result);

        Assert.Equal(expectedStatusCode, statusCode);
        Assert.True(body.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task Failure_WritesErrorEnvelopeWithCodeFieldAndTypeAsString()
    {
        EndpointResult<int> result = Result.Failure<int, Error>(GeneralErrors.ValueIsRequired("Email"));

        (_, JsonElement body) = await ExecuteAsync(result);

        JsonElement error = body.GetProperty("error");
        JsonElement message = error.GetProperty("messages")[0];
        Assert.Equal("VALIDATION", error.GetProperty("type").GetString());
        Assert.Equal("value.is.required", message.GetProperty("code").GetString());
        Assert.Equal("Email", message.GetProperty("invalidField").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task Success_WritesValueInEnvelopeWith200()
    {
        EndpointResult<int> result = Result.Success<int, Error>(42);

        (int statusCode, JsonElement body) = await ExecuteAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.Equal(42, body.GetProperty("result").GetInt32());
        Assert.False(body.GetProperty("isError").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task UnitSuccess_WritesEmptyEnvelopeWith200()
    {
        EndpointResult result = UnitResult.Success<Error>();

        (int statusCode, JsonElement body) = await ExecuteAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.False(body.GetProperty("isError").GetBoolean());
    }

    private static Error CreateError(ErrorType errorType) =>
        errorType switch
        {
            ErrorType.VALIDATION => Error.Validation("test", "test"),
            ErrorType.NOT_FOUND => Error.NotFound("test", "test"),
            ErrorType.CONFLICT => Error.Conflict("test", "test"),
            ErrorType.AUTHENTICATION => Error.Authentication("test", "test"),
            ErrorType.AUTHORIZATION => Error.Authorization("test", "test"),
            ErrorType.RATE_LIMIT => Error.RateLimit("test", "test"),
            _ => Error.Failure("test", "test"),
        };

    private static async Task<(int StatusCode, JsonElement Body)> ExecuteAsync(IResult result)
    {
        var httpContext = new DefaultHttpContext();
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await result.ExecuteAsync(httpContext);

        body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(body);
        return (httpContext.Response.StatusCode, document.RootElement.Clone());
    }
}
