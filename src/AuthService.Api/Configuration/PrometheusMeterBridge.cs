using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Prometheus;

namespace AuthService.Api.Configuration;

/// <summary>
/// Переносит счётчики и гистограммы .NET (<see cref="Counter{T}"/>, <see cref="Histogram{T}"/> из <see cref="Meter"/>) в Prometheus настоящими counter и histogram. Встроенный адаптер
/// prometheus-net публикует их как gauge без суффикса <c>_total</c>: на таких рядах <c>rate()</c> и <c>increase()</c> считают
/// неверно после перезапуска сервиса, а оповещения по ним ложные. Имя в Prometheus — <c>{meter}_{instrument}_total</c>, метки — теги
/// измерения. Набор меток у каждого счётчика один и тот же во всём коде (иначе prometheus-net откажет, и измерение пропадёт с записью в лог).
/// </summary>
public sealed class PrometheusMeterBridge : IHostedService, IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly HashSet<string> _meters;
    private readonly ILogger<PrometheusMeterBridge> _logger;
    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Histogram> _histograms = new(StringComparer.Ordinal);

    private readonly Action? _seed;

    /// <param name="meters">Имена Meter, чьи счётчики переносятся.</param>
    /// <param name="logger">Журнал.</param>
    /// <param name="seed">Вызывается после старта: создаёт ряды со значением 0, чтобы первое событие давало приращение.</param>
    public PrometheusMeterBridge(IEnumerable<string> meters, ILogger<PrometheusMeterBridge> logger, Action? seed = null)
    {
        _seed = seed;
        _meters = [.. meters];
        _logger = logger;
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument is Counter<long> or Histogram<double> && _meters.Contains(instrument.Meter.Name))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(OnMeasurement);
        _listener.SetMeasurementEventCallback<double>(OnHistogram);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        _seed?.Invoke();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _listener.Dispose();

    private void OnHistogram(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        (string[] keys, string[] values) = Labels(tags);
        string name = $"{instrument.Meter.Name}_{instrument.Name}";
        try
        {
            // 5 мс … ~10 с: от ответа из кэша до таймаута.
            Histogram histogram = _histograms.GetOrAdd(
                name + "|" + string.Join(',', keys),
                _ => Metrics.CreateHistogram(
                    name, instrument.Description ?? name, new HistogramConfiguration { LabelNames = keys, Buckets = Histogram.ExponentialBuckets(0.005, 2, 12) }));
            histogram.WithLabels(values).Observe(value);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Histogram {Histogram} was not exported to Prometheus (labels: {Labels})", name, string.Join(',', keys));
        }
    }

    // Порядок тегов в измерениях не фиксирован: сортируем по имени, чтобы набор меток был одним и тем же.
    private static (string[] Keys, string[] Values) Labels(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string[] keys = new string[tags.Length];
        string[] values = new string[tags.Length];
        for (int i = 0; i < tags.Length; i++)
        {
            keys[i] = tags[i].Key;
            values[i] = tags[i].Value?.ToString() ?? string.Empty;
        }

        Array.Sort(keys, values, StringComparer.Ordinal);
        return (keys, values);
    }

    private void OnMeasurement(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        (string[] keys, string[] values) = Labels(tags);
        string name = $"{instrument.Meter.Name}_{instrument.Name}_total";
        try
        {
            Counter counter = _counters.GetOrAdd(
                name + "|" + string.Join(',', keys),
                _ => Metrics.CreateCounter(name, instrument.Description ?? name, new CounterConfiguration { LabelNames = keys }));
            counter.WithLabels(values).Inc(value);
        }
        catch (Exception exception)
        {
            // Тот же счётчик с другим набором меток prometheus-net не принимает: измерение пропадает, но не роняет запрос (метрики — не повод
            // для отказа в обслуживании).
            _logger.LogError(exception, "Counter {Counter} was not exported to Prometheus (labels: {Labels})", name, string.Join(',', keys));
        }
    }
}
