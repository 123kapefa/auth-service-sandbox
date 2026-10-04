using System.Net;
using AuthService.Api.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthService.UnitTests;

// Настоящий ForwardedHeadersMiddleware с нашими настройками: проверяем, чьим заголовкам он верит.
public sealed class ForwardedHeadersConfigurationTests
{
    private const string CLIENT_IP = "203.0.113.7";

    [Theory]
    [InlineData("172.18.0.5")]   // nginx в сети compose
    [InlineData("127.0.0.1")]    // loopback
    public async Task TrustedProxy_HeadersApplied(string proxyIp)
    {
        HttpContext context = await SendThroughMiddlewareAsync(proxyIp, BuildOptions());

        Assert.Equal(IPAddress.Parse(CLIENT_IP), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task UntrustedSource_HeadersIgnored()
    {
        HttpContext context = await SendThroughMiddlewareAsync("198.51.100.20", BuildOptions());

        Assert.Equal(IPAddress.Parse("198.51.100.20"), context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task ConfiguredNetworks_ReplaceDefaults()
    {
        ForwardedHeadersOptions options = BuildOptions(new() { [$"{ForwardedHeadersConfiguration.TRUSTED_NETWORKS_KEY}:0"] = "10.0.0.0/8" });

        HttpContext fromDocker = await SendThroughMiddlewareAsync("172.18.0.5", options);
        HttpContext fromConfigured = await SendThroughMiddlewareAsync("10.1.2.3", options);

        Assert.Equal("http", fromDocker.Request.Scheme);
        Assert.Equal("https", fromConfigured.Request.Scheme);
    }

    [Fact]
    public void InvalidNetwork_FailsAtStartup()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            BuildOptions(new() { [$"{ForwardedHeadersConfiguration.TRUSTED_NETWORKS_KEY}:0"] = "172.16.0.0/99" }));

        Assert.Contains("172.16.0.0/99", exception.Message, StringComparison.Ordinal);
    }

    private static ForwardedHeadersOptions BuildOptions(Dictionary<string, string?>? settings = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        ServiceProvider provider = new ServiceCollection()
            .AddAuthServiceForwardedHeaders(configuration)
            .BuildServiceProvider();

        return provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    private static async Task<HttpContext> SendThroughMiddlewareAsync(string remoteIp, ForwardedHeadersOptions options)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = CLIENT_IP;
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        await middleware.Invoke(context);

        return context;
    }
}
