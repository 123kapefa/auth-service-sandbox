using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using AuthService.Core;
using AuthService.Infrastructure.Keycloak;
using Xunit;

namespace AuthService.UnitTests;

/// <summary>13.1: запросы к Keycloak считаются по цели и исходу, время ответа пишется в гистограмму; в метках нет путей и realm.</summary>
public sealed class KeycloakMetricsHandlerTests : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Name, double Value, Dictionary<string, string?> Tags)> _seen = new();

    public KeycloakMetricsHandlerTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, AuthMetrics.METER_NAME, StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    [Theory]
    [InlineData("/realms/shop/protocol/openid-connect/token", HttpStatusCode.OK, "token", "2xx")]
    [InlineData("/realms/shop/protocol/openid-connect/token", HttpStatusCode.BadRequest, "token", "4xx")]
    [InlineData("/realms/shop/protocol/openid-connect/logout", HttpStatusCode.NoContent, "logout", "2xx")]
    [InlineData("/realms/shop/protocol/openid-connect/certs", HttpStatusCode.OK, "keys", "2xx")]
    [InlineData("/realms/shop/.well-known/openid-configuration", HttpStatusCode.OK, "keys", "2xx")]
    [InlineData("/admin/realms/shop/users", HttpStatusCode.InternalServerError, "admin", "5xx")]
    [InlineData("/realms/shop/clients-registrations/default", HttpStatusCode.Unauthorized, "registration", "4xx")]
    [InlineData("/realms/shop/platform-codes/settings", HttpStatusCode.OK, "other", "2xx")]
    public async Task Request_CountedByTargetAndStatusClass(string path, HttpStatusCode status, string target, string outcome)
    {
        using var client = new HttpClient(new KeycloakMetricsHandler { InnerHandler = new Stub(status) });

        using HttpResponseMessage response = await client.GetAsync(new Uri("http://keycloak:8180" + path));

        Assert.Equal(1, Sum("keycloak_requests", ("target", target), ("outcome", outcome)));
        Assert.Equal(1, Sum("keycloak_request_duration_seconds", ("target", target)));
    }

    [Fact]
    public async Task ConnectionFailure_CountedAsError_AndRethrown()
    {
        using var client = new HttpClient(new KeycloakMetricsHandler { InnerHandler = new Stub(null) });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PostAsync(new Uri("http://keycloak:8180/realms/shop/protocol/openid-connect/token"), content: null));

        Assert.Equal(1, Sum("keycloak_requests", ("target", "token"), ("outcome", "error")));
    }

    [Fact]
    public async Task CancelledByCaller_NotCountedAsKeycloakFailure()
    {
        using var client = new HttpClient(new KeycloakMetricsHandler { InnerHandler = new Stub(null) });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync(new Uri("http://keycloak:8180/realms/shop/protocol/openid-connect/certs"), cts.Token));

        Assert.Equal(0, Sum("keycloak_requests", ("target", "keys"), ("outcome", "error")));
    }

    [Fact]
    public async Task Labels_NeverCarryRealmOrPath()
    {
        using var client = new HttpClient(new KeycloakMetricsHandler { InnerHandler = new Stub(HttpStatusCode.OK) });

        using HttpResponseMessage response = await client.GetAsync(new Uri("http://keycloak:8180/admin/realms/secret-shop/users/123"));

        Assert.DoesNotContain(_seen.SelectMany(m => m.Tags.Values), value => value is not null && value.Contains("secret-shop", StringComparison.Ordinal));
    }

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        _seen.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString(), StringComparer.Ordinal)));

    private long Sum(string name, params (string Key, string Value)[] tags) =>
        _seen.Count(m => string.Equals(m.Name, name, StringComparison.Ordinal)
            && tags.All(tag => m.Tags.TryGetValue(tag.Key, out string? value) && string.Equals(value, tag.Value, StringComparison.Ordinal)));

    private sealed class Stub(HttpStatusCode? status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            status is { } code
                ? Task.FromResult(new HttpResponseMessage(code))
                : throw new HttpRequestException("connection refused");
    }
}
