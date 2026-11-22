using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>
/// Записывает измерения счётчиков по имени Meter. Счётчики общие на процесс (тесты идут параллельно), поэтому проверяется
/// «выросло не меньше чем на N» по конкретному набору меток, а не абсолютное значение.
/// </summary>
public sealed class MetricsRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Instrument, long Value, Dictionary<string, string?> Tags)> _measurements = new();

    public MetricsRecorder(params string[] meters)
    {
        HashSet<string> names = [.. meters];
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (names.Contains(instrument.Meter.Name))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _measurements.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString(), StringComparer.Ordinal))));
        _listener.Start();
    }

    /// <summary>Сумма измерений счётчика с этими метками (остальные метки не важны).</summary>
    public long Sum(string instrument, params (string Key, string Value)[] tags) =>
        _measurements
            .Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal)
                && tags.All(tag => m.Tags.TryGetValue(tag.Key, out string? value) && string.Equals(value, tag.Value, StringComparison.Ordinal)))
            .Sum(m => m.Value);

    /// <summary>Все значения меток счётчика (для проверки, что в метках нет ничего лишнего).</summary>
    public IEnumerable<string?> TagValues(string instrument) =>
        _measurements.Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal)).SelectMany(m => m.Tags.Values);

    public void Dispose() => _listener.Dispose();
}
