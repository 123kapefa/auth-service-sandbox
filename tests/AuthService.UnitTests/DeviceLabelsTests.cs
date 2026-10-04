using AuthService.Core.Features.Security;
using AuthService.Domain.Sessions;
using Xunit;

namespace AuthService.UnitTests;

public sealed class DeviceLabelsTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36", "Chrome · macOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Mobile/15E148 Safari/604.1", "Safari · iPhone")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:143.0) Gecko/20100101 Firefox/143.0", "Firefox · Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0", "Edge · Windows")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Mobile Safari/537.36", "Chrome · Android")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 YaBrowser/25.8.0.0 Safari/537.36", "Яндекс Браузер · Windows")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 18_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/141.0 Mobile/15E148 Safari/604.1", "Chrome · iPad")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 OPR/122.0.0.0", "Opera · Linux")]
    [InlineData("curl/8.5.0", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void FromUserAgent(string? userAgent, string? expected) =>
        Assert.Equal(expected, DeviceLabels.FromUserAgent(userAgent));

    [Fact]
    public void LoginDevice_LongUserAgent_Trimmed_EmptyIsNull()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.Equal(LoginDevice.MAX_USER_AGENT_LENGTH, LoginDevice.Record("s", Guid.Empty, new string('a', 1000), now).UserAgent!.Length);
        Assert.Null(LoginDevice.Record("s", Guid.Empty, "  ", now).UserAgent);
    }
}
