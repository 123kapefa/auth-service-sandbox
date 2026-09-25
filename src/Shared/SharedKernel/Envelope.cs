using System.Text.Json.Serialization;

namespace SharedKernel;

public sealed record Envelope
{
    [JsonConstructor]
    private Envelope(object? result, Error? error)
    {
        Result = result;
        Error = error;
        TimeGenerated = DateTime.UtcNow;
    }

    public object? Result { get; }

    public Error? Error { get; }

    public bool IsError => Error is not null;

    public DateTime TimeGenerated { get; }

    public static Envelope Ok(object? result = null) => new(result, null);

    public static Envelope Fail(Error error) => new(null, error);
}

public sealed record Envelope<T>
{
    [JsonConstructor]
    private Envelope(T? result, Error? error)
    {
        Result = result;
        Error = error;
        TimeGenerated = DateTime.UtcNow;
    }

    public T? Result { get; }

    public Error? Error { get; }

    public bool IsError => Error is not null;

    public DateTime TimeGenerated { get; }

    public static Envelope<T> Ok(T? result = default) => new(result, null);

    public static Envelope<T> Fail(Error error) => new(default, error);
}
