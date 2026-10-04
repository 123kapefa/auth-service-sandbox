using System.Net;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.IntegrationTests.Infrastructure;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Раздел «Безопасность» кабинета (/me/security): свои учётные данные с сервера входа, без секретов.</summary>
public sealed class SecurityTests : IClassFixture<AuthServiceWebFactory>
{
    private static readonly DateTimeOffset _monday = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly AuthServiceWebFactory _factory;

    public SecurityTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        factory.IdentityAdmin.Reset();
        factory.Revocations.Reset();
        factory.IdentityAdmin.Add(TestUser.Customer, []);
        factory.IdentityAdmin.Add(TestUser.AdminInAdminMode, [], hasPassword: false);
        factory.IdentityAdmin.AddCredential(TestUser.Customer, new UserCredential("otp-2", Credentials.OTP, "Планшет", _monday.AddDays(1)));
        factory.IdentityAdmin.AddCredential(TestUser.Customer, new UserCredential("otp-1", Credentials.OTP, "Телефон", _monday));
        factory.IdentityAdmin.AddCredential(TestUser.Customer, new UserCredential("key-1", Credentials.PASSKEY, "MacBook", _monday));
        factory.IdentityAdmin.AddCredential(
            TestUser.Customer, new UserCredential("codes", Credentials.RECOVERY_CODES, null, _monday, Remaining: 10, Total: 12));
        factory.IdentityAdmin.AddCredential(
            TestUser.Customer, new UserCredential("codes-old", Credentials.RECOVERY_CODES, null, _monday.AddDays(-7), Remaining: 1, Total: 12));
    }

    [Fact]
    public async Task Security_Own_GroupedByKind_OldestFirst()
    {
        JsonElement result = await ResultAsync(TestUser.Customer);

        Assert.True(result.GetProperty("hasPassword").GetBoolean());
        Assert.Equal(["otp-1:Телефон", "otp-2:Планшет"], Items(result, "authenticators"));
        Assert.Equal(["key-1:MacBook"], Items(result, "passkeys"));
        JsonElement codes = result.GetProperty("recoveryCodes");
        Assert.Equal(("codes", 10, 12), (codes.GetProperty("id").GetString(), codes.GetProperty("remaining").GetInt32(), codes.GetProperty("total").GetInt32()));
    }

    [Fact]
    public async Task Security_NothingConnected_EmptyLists_NoCodes()
    {
        JsonElement result = await ResultAsync(TestUser.AdminInAdminMode);

        Assert.False(result.GetProperty("hasPassword").GetBoolean());
        Assert.Empty(Items(result, "authenticators"));
        Assert.Empty(Items(result, "passkeys"));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("recoveryCodes").ValueKind);
    }

    [Fact]
    public async Task Security_Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/me/security", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<JsonElement> ResultAsync(TestUser user)
    {
        using HttpClient client = _factory.CreateClientFor(user);
        HttpResponseMessage response = await client.GetAsync(new Uri("/me/security", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("result").Clone();
    }

    private static string[] Items(JsonElement result, string name) =>
        [.. result.GetProperty(name).EnumerateArray().Select(item => $"{item.GetProperty("id").GetString()}:{item.GetProperty("label").GetString()}")];
}
