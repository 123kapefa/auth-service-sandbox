using Microsoft.AspNetCore.Http;
using SharedKernel;

namespace Framework.Endpoints;

internal sealed class ErrorResult(Error error) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = ToStatusCode(error.Type);
        return httpContext.Response.WriteAsJsonAsync(Envelope.Fail(error));
    }

    internal static int ToStatusCode(ErrorType errorType) =>
        errorType switch
        {
            ErrorType.VALIDATION => StatusCodes.Status400BadRequest,
            ErrorType.NOT_FOUND => StatusCodes.Status404NotFound,
            ErrorType.CONFLICT => StatusCodes.Status409Conflict,
            ErrorType.AUTHENTICATION => StatusCodes.Status401Unauthorized,
            ErrorType.AUTHORIZATION => StatusCodes.Status403Forbidden,
            ErrorType.RATE_LIMIT => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError,
        };
}
