using System.Reflection;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using SharedKernel;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Framework.Endpoints;

/// <summary>Ответ эндпоинта без значения: UnitResult → 200 Envelope.Ok() или ошибка в Envelope.</summary>
public sealed class EndpointResult : IResult, IEndpointMetadataProvider
{
    private readonly IResult _result;

    public EndpointResult(UnitResult<Error> result) =>
        _result = result.IsSuccess ? new SuccessResult() : new ErrorResult(result.Error);

    public Task ExecuteAsync(HttpContext httpContext) => _result.ExecuteAsync(httpContext);

    public static implicit operator EndpointResult(UnitResult<Error> result) => new(result);

    public static EndpointResult FromUnitResult(UnitResult<Error> result) => new(result);

    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        EndpointResultMetadata.Add(builder, typeof(Envelope));
    }
}

/// <summary>Ответ эндпоинта со значением: Result&lt;T, Error&gt; → 200 Envelope&lt;T&gt; или ошибка в Envelope.</summary>
public sealed class EndpointResult<TValue> : IResult, IEndpointMetadataProvider
{
    private readonly IResult _result;

    public EndpointResult(Result<TValue, Error> result) =>
        _result = result.IsSuccess ? new SuccessResult<TValue>(result.Value) : new ErrorResult(result.Error);

    public Task ExecuteAsync(HttpContext httpContext) => _result.ExecuteAsync(httpContext);

    public static implicit operator EndpointResult<TValue>(Result<TValue, Error> result) => new(result);

    public static EndpointResult<TValue> FromResult(Result<TValue, Error> result) => new(result);

    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        EndpointResultMetadata.Add(builder, typeof(Envelope<TValue>));
    }
}

/// <summary>Описывает возможные ответы эндпоинта для OpenAPI: успех и все коды из ErrorResult.</summary>
internal static class EndpointResultMetadata
{
    private static readonly string[] _jsonContentTypes = ["application/json"];

    private static readonly int[] _errorStatusCodes =
    [
        StatusCodes.Status400BadRequest,
        StatusCodes.Status401Unauthorized,
        StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status429TooManyRequests,
        StatusCodes.Status500InternalServerError,
    ];

    public static void Add(EndpointBuilder builder, Type successEnvelopeType)
    {
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, successEnvelopeType, _jsonContentTypes));
        foreach (int statusCode in _errorStatusCodes)
        {
            builder.Metadata.Add(new ProducesResponseTypeMetadata(statusCode, typeof(Envelope), _jsonContentTypes));
        }
    }
}
