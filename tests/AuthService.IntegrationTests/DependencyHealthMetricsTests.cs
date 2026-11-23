using System.Text;
using AuthService.Api.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>13.3: готовность базы и Redis — метрика auth_dependency_up (по ней оповещения «зависимость недоступна»).</summary>
public sealed class DependencyHealthMetricsTests
{
    [Fact]
    public async Task Publish_SetsOneForHealthyAndZeroForFailed_UnderDependencyNames()
    {
        var publisher = new DependencyHealthMetrics();
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["postgresql"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
                ["token-revocation-redis"] = new(HealthStatus.Unhealthy, "Redis is unavailable.", TimeSpan.Zero, null, null),
            },
            TimeSpan.Zero);

        await publisher.PublishAsync(report, CancellationToken.None);

        string text = await Export();
        Assert.Contains("auth_dependency_up{dependency=\"database\"} 1", text, StringComparison.Ordinal);
        Assert.Contains("auth_dependency_up{dependency=\"redis\"} 0", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_RecordsWhenTheChecksLastRan()
    {
        var publisher = new DependencyHealthMetrics();
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await publisher.PublishAsync(new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero), CancellationToken.None);

        string line = (await Export()).Split('\n').First(l => l.StartsWith("auth_dependency_checked_timestamp_seconds ", StringComparison.Ordinal));
        Assert.InRange(long.Parse(line.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture), before, before + 5);
    }

    [Fact]
    public async Task Publish_RecoveryFlipsBackToOne()
    {
        var publisher = new DependencyHealthMetrics();
        HealthReport Report(HealthStatus status) => new(
            new Dictionary<string, HealthReportEntry> { ["postgresql"] = new(status, null, TimeSpan.Zero, null, null) }, TimeSpan.Zero);

        await publisher.PublishAsync(Report(HealthStatus.Unhealthy), CancellationToken.None);
        Assert.Contains("auth_dependency_up{dependency=\"database\"} 0", await Export(), StringComparison.Ordinal);

        await publisher.PublishAsync(Report(HealthStatus.Healthy), CancellationToken.None);
        Assert.Contains("auth_dependency_up{dependency=\"database\"} 1", await Export(), StringComparison.Ordinal);
    }

    private static async Task<string> Export()
    {
        using var stream = new MemoryStream();
        await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
