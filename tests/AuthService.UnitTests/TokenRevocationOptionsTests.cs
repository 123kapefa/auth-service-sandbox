using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PlatformAuth.Redis;
using Xunit;

namespace AuthService.UnitTests;

public sealed class TokenRevocationOptionsTests
{
    [Theory]
    [InlineData(null, StoreFailureMode.Open)]          // по умолчанию — как было: токен пропускается
    [InlineData("Open", StoreFailureMode.Open)]
    [InlineData("Closed", StoreFailureMode.Closed)]
    [InlineData("closed", StoreFailureMode.Closed)]
    public void OnStoreFailure_DefaultOpen_ClosedOnRequest(string? value, StoreFailureMode expected) =>
        Assert.Equal(expected, Options(value).Value.OnStoreFailure);

    [Theory]
    [InlineData("Clsed")]
    [InlineData("fail")]
    [InlineData("2")]
    public void OnStoreFailure_Typo_IsAnError(string value) =>
        Assert.ThrowsAny<Exception>(() => Options(value).Value);

    [Fact]
    public async Task OnStoreFailure_Typo_StopsHostStart()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("ConnectionStrings:Redis", "localhost:6379"),
            new KeyValuePair<string, string?>("TokenRevocation:OnStoreFailure", "Clsed"),
        ]);
        builder.Services.AddPlatformTokenRevocation(builder.Configuration);
        using IHost host = builder.Build();
        await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());
    }

    private static IOptions<TokenRevocationOptions> Options(string? onStoreFailure)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:Redis", "localhost:6379"),
                new KeyValuePair<string, string?>("TokenRevocation:OnStoreFailure", onStoreFailure),
            ])
            .Build();
        var services = new ServiceCollection();
        services.AddPlatformTokenRevocation(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<TokenRevocationOptions>>();
    }
}
