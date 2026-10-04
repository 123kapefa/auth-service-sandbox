using Microsoft.AspNetCore.Http;
using SharedKernel;

namespace Framework.Endpoints;

internal sealed class SuccessResult : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        return httpContext.Response.WriteAsJsonAsync(Envelope.Ok());
    }
}

internal sealed class SuccessResult<TValue>(TValue? value) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        return httpContext.Response.WriteAsJsonAsync(Envelope<TValue>.Ok(value));
    }
}
