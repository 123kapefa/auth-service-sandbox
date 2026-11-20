using AuthService.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.UnitTests;

public sealed class CookieDomainRejectionTests
{
    [Theory]
    [InlineData(".example.com")]
    [InlineData("shop.example.com")]
    public void AddCore_CookiesDomainSet_StopsStartupAndExplains(string domain)
    {
        IConfiguration configuration = Configuration(("Cookies:Domain", domain));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddCore(configuration));

        Assert.Contains("Cookies:Domain", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AddCore_CookiesDomainEmpty_Starts(string? domain)
    {
        IConfiguration configuration = Configuration(("Cookies:Domain", domain));

        Exception? error = Record.Exception(() => new ServiceCollection().AddCore(configuration));

        Assert.Null(error);
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build();
}
