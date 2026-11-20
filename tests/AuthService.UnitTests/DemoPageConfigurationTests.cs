using AuthService.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AuthService.UnitTests;

public sealed class DemoPageConfigurationTests
{
    [Theory]
    [InlineData("Development", "true", true)]
    [InlineData("Docker", "true", true)]
    [InlineData("Development", "false", false)]
    [InlineData("Docker", null, false)]            // без настройки — закрыта
    [InlineData("Production", null, false)]
    [InlineData("Production", "false", false)]
    [InlineData("Staging", null, false)]
    public void IsEnabled_OnlyWhenSetExplicitly(string environment, string? setting, bool expected) =>
        Assert.Equal(expected, DemoPageConfiguration.IsEnabled(Configuration(setting), environment));

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    public void IsEnabled_ProductionWithDemoPageOn_StopsStartupAndExplains(string environment)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => DemoPageConfiguration.IsEnabled(Configuration("true"), environment));

        Assert.Contains("DemoPage:Enabled", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsEnabled_NotABoolean_StopsStartup() =>
        Assert.ThrowsAny<Exception>(() => DemoPageConfiguration.IsEnabled(Configuration("maybe"), "Docker"));

    private static IConfiguration Configuration(string? enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(DemoPageConfiguration.ENABLED_KEY, enabled)])
            .Build();
}
