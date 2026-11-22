using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Записывает всё, что сервис пишет в журнал, вместе с областями (TraceId, Project…): для проверки журнала в тестах.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    /// <summary>Запись журнала: категория, уровень, текст (с исключением) и области одной строкой каждая.</summary>
    public sealed record LogEntry(string Category, LogLevel Level, string Text, IReadOnlyList<string> Scopes)
    {
        /// <summary>Всё, что видит читающий журнал: текст и области.</summary>
        public string Everything => Text + "\n" + string.Join('\n', Scopes);
    }

    private sealed class CapturingLogger(string category, CapturingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<string>();
            provider._scopes.ForEachScope(
                static (scope, list) =>
                    list.Add(scope is IEnumerable<KeyValuePair<string, object?>> pairs
                        ? string.Join(", ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                        : scope?.ToString() ?? string.Empty),
                scopes);
            provider._entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception), scopes));
        }
    }
}
