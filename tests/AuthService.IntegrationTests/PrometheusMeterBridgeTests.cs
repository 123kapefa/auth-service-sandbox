using System.Diagnostics.Metrics;
using System.Text;
using AuthService.Api.Configuration;
using AuthService.Core;
using PlatformAuth;
using Microsoft.Extensions.Logging.Abstractions;
using Prometheus;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>13.1: счётчики .NET уходят в Prometheus настоящими counter с суффиксом _total и метками из тегов.</summary>
public sealed class PrometheusMeterBridgeTests
{
    // Встроенный адаптер prometheus-net по умолчанию публикует все Meter процесса и дублировал бы наши счётчики; в сервисе он
    // ограничен списком встроенных источников (MetricsConfiguration), здесь выключен совсем.
    static PrometheusMeterBridgeTests() => Metrics.ConfigureMeterAdapter(options => options.InstrumentFilterPredicate = _ => false);

    [Fact]
    public async Task Counter_ExportedAsPrometheusCounter_WithLabels()
    {
        const string meterName = "bridgetest_a";
        using var meter = new Meter(meterName);
        Counter<long> counter = meter.CreateCounter<long>("things", description: "Вещи");
        using var bridge = new PrometheusMeterBridge([meterName], NullLogger<PrometheusMeterBridge>.Instance);
        await bridge.StartAsync(CancellationToken.None);

        counter.Add(2, new KeyValuePair<string, object?>("kind", "x"), new KeyValuePair<string, object?>("result", "ok"));
        counter.Add(1, new KeyValuePair<string, object?>("result", "ok"), new KeyValuePair<string, object?>("kind", "x"));
        counter.Add(1, new KeyValuePair<string, object?>("kind", "y"), new KeyValuePair<string, object?>("result", "error"));

        string text = await Export();
        Assert.Contains("# TYPE bridgetest_a_things_total counter", text, StringComparison.Ordinal);
        Assert.Contains("bridgetest_a_things_total{kind=\"x\",result=\"ok\"} 3", text, StringComparison.Ordinal);
        Assert.Contains("bridgetest_a_things_total{kind=\"y\",result=\"error\"} 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CounterWithoutTags_Exported()
    {
        const string meterName = "bridgetest_b";
        using var meter = new Meter(meterName);
        Counter<long> counter = meter.CreateCounter<long>("started");
        using var bridge = new PrometheusMeterBridge([meterName], NullLogger<PrometheusMeterBridge>.Instance);
        await bridge.StartAsync(CancellationToken.None);

        counter.Add(1);
        counter.Add(1);

        Assert.Contains("bridgetest_b_started_total 2", await Export(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherMeters_NotExported_DifferentLabelSet_DoesNotThrow()
    {
        const string meterName = "bridgetest_c";
        using var meter = new Meter(meterName);
        Counter<long> counter = meter.CreateCounter<long>("mixed");
        using var other = new Meter("bridgetest_other");
        Counter<long> ignored = other.CreateCounter<long>("ignored");
        using var bridge = new PrometheusMeterBridge([meterName], NullLogger<PrometheusMeterBridge>.Instance);
        await bridge.StartAsync(CancellationToken.None);

        counter.Add(1, new KeyValuePair<string, object?>("a", "1"));
        counter.Add(1, new KeyValuePair<string, object?>("b", "1")); // другой набор меток: измерение пропадает, запрос не падает
        ignored.Add(1);

        string text = await Export();
        Assert.Contains("bridgetest_c_mixed_total{a=\"1\"} 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("bridgetest_other", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Histogram_ExportedWithBucketsAndLabels()
    {
        const string meterName = "bridgetest_d";
        using var meter = new Meter(meterName);
        Histogram<double> histogram = meter.CreateHistogram<double>("latency_seconds", unit: "s", description: "Время");
        using var bridge = new PrometheusMeterBridge([meterName], NullLogger<PrometheusMeterBridge>.Instance);
        await bridge.StartAsync(CancellationToken.None);

        histogram.Record(0.03, new KeyValuePair<string, object?>("target", "token"));
        histogram.Record(0.5, new KeyValuePair<string, object?>("target", "token"));

        string text = await Export();
        Assert.Contains("# TYPE bridgetest_d_latency_seconds histogram", text, StringComparison.Ordinal);
        Assert.Contains("bridgetest_d_latency_seconds_count{target=\"token\"} 2", text, StringComparison.Ordinal);
        Assert.Contains("bridgetest_d_latency_seconds_bucket{target=\"token\",le=\"0.04\"} 1", text, StringComparison.Ordinal);
    }

    // Ряд, появившийся сразу со значением 1, Prometheus не видит как приращение: первый сбой пропал бы из increase() и оповещений.
    [Fact]
    public async Task Seed_CreatesZeroSeries_SoFirstEventIsAnIncrease()
    {
        using var bridge = new PrometheusMeterBridge(
            [AuthMetrics.METER_NAME, PlatformAuthMetrics.METER_NAME],
            NullLogger<PrometheusMeterBridge>.Instance,
            seed: () =>
            {
                AuthMetrics.Seed();
                PlatformAuthMetrics.Seed();
            });
        await bridge.StartAsync(CancellationToken.None);

        string text = await Export();

        Assert.Contains("auth_logins_total{result=\"exchange_error\"}", text, StringComparison.Ordinal);
        Assert.Contains("auth_refreshes_total{result=\"rejected\"}", text, StringComparison.Ordinal);
        Assert.Contains("auth_keycloak_requests_total{outcome=\"5xx\",target=\"token\"}", text, StringComparison.Ordinal);
        Assert.Contains("platformauth_revocation_store_errors_total{kind=\"connection\",mode=\"closed\"}", text, StringComparison.Ordinal);
        Assert.Contains("platformauth_token_checks_total{status=\"unavailable\"}", text, StringComparison.Ordinal);
        Assert.Contains("auth_logins_started_total", text, StringComparison.Ordinal);
    }

    private static async Task<string> Export()
    {
        using var stream = new MemoryStream();
        await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
