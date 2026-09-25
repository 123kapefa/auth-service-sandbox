using System.Text.Json.Serialization;

namespace SharedKernel;

public sealed record ErrorMessage(string Code, string Message, string? InvalidField = null);

public sealed record Error
{
    [JsonConstructor]
    private Error(IReadOnlyList<ErrorMessage> messages, ErrorType type, ErrorBehavior behavior = ErrorBehavior.Permanent)
    {
        Messages = [.. messages];
        Type = type;
        Behavior = behavior;
    }

    public IReadOnlyList<ErrorMessage> Messages { get; }

    public ErrorType Type { get; }

    public ErrorBehavior Behavior { get; }

    public string GetMessage() => string.Join(';', Messages.Select(message => message.ToString()));

    public static Error Validation(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.VALIDATION);

    public static Error Validation(IEnumerable<ErrorMessage> messages) =>
        new([.. messages], ErrorType.VALIDATION);

    public static Error NotFound(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.NOT_FOUND);

    public static Error Failure(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.FAILURE);

    public static Error Conflict(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.CONFLICT);

    public static Error Authentication(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.AUTHENTICATION);

    public static Error Authorization(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.AUTHORIZATION);

    public static Error RateLimit(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.RATE_LIMIT);

    public Error AsTransient() => new(Messages, Type, ErrorBehavior.Transient);

    public Error AsPermanent() => new(Messages, Type, ErrorBehavior.Permanent);
}

[JsonConverter(typeof(JsonStringEnumConverter<ErrorType>))]
public enum ErrorType
{
    VALIDATION,
    NOT_FOUND,
    FAILURE,
    CONFLICT,
    AUTHENTICATION,
    AUTHORIZATION,
    RATE_LIMIT,
}

[JsonConverter(typeof(JsonStringEnumConverter<ErrorBehavior>))]
public enum ErrorBehavior
{
    Permanent,
    Transient,
}
