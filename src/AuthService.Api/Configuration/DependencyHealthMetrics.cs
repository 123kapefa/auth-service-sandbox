using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;

namespace AuthService.Api.Configuration;

/// <summary>
/// Готовность зависимостей сервиса как метрика: <c>auth_dependency_up{dependency}</c> — 1, если проверка готовности прошла, 0, если нет
/// (база: <c>database</c>, хранилище отметок отзыва: <c>redis</c>). Проверки здоровья выполняются сами, раз в несколько секунд, а не
/// когда кто-то спросит /health/ready: оповещение «база или Redis недоступны» не должно зависеть от того, ходит ли к сервису проба.
/// </summary>
public sealed class DependencyHealthMetrics : IHealthCheckPublisher
{
    // Имя проверки здоровья → имя зависимости в метке; неизвестные проверки публикуются под своим именем.
    private static readonly Dictionary<string, string> _names = new(StringComparer.Ordinal)
    {
        ["postgresql"] = "database",
        ["token-revocation-redis"] = "redis",
    };

    private readonly Gauge _up = Metrics.CreateGauge(
        "auth_dependency_up",
        "Зависимость сервиса отвечает: 1 — проверка готовности прошла, 0 — нет (database, redis).",
        new GaugeConfiguration { LabelNames = ["dependency"] });

    // Время последней проверки: если проверки зависли, auth_dependency_up так и осталась бы единицей. Эту ситуацию ловит оповещение AuthDependencyChecksStale.
    private readonly Gauge _checked = Metrics.CreateGauge(
        "auth_dependency_checked_timestamp_seconds",
        "Время последней проверки готовности зависимостей (секунды Unix): не обновляется — auth_dependency_up устарела.");

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        _checked.Set(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        foreach ((string name, HealthReportEntry entry) in report.Entries)
        {
            _up.WithLabels(_names.GetValueOrDefault(name, name)).Set(entry.Status == HealthStatus.Healthy ? 1 : 0);
        }

        return Task.CompletedTask;
    }
}
