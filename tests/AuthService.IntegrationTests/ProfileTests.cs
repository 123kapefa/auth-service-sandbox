using System.Net;
using System.Text.Json;
using AuthService.IntegrationTests.Infrastructure;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Профиль личного кабинета (/me/profile): свой, с сервера входа.</summary>
public sealed class ProfileTests : IClassFixture<AuthServiceWebFactory>
{
    private readonly AuthServiceWebFactory _factory;

    public ProfileTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        factory.IdentityAdmin.Reset();
        factory.Revocations.Reset();
        factory.IdentityAdmin.Add(TestUser.Customer, [], email: "customer@sandbox.test", emailVerified: true);
        factory.IdentityAdmin.Add(TestUser.AdminInAdminMode, []);
    }

    [Fact]
    public async Task Profile_Own_PhoneAndEmailFromServer()
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.Customer);

        HttpResponseMessage response = await client.GetAsync(new Uri("/me/profile", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement profile = body.RootElement.GetProperty("result");
        Assert.Equal(TestUser.Customer.Id, profile.GetProperty("id").GetGuid());
        Assert.Equal(TestUser.Customer.Phone, profile.GetProperty("phone").GetString());
        Assert.Equal("customer@sandbox.test", profile.GetProperty("email").GetString());
        Assert.True(profile.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("phoneChangedAt").ValueKind);
    }

    [Fact]
    public async Task Profile_PhoneChanged_WhenShown()
    {
        var at = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        _factory.IdentityAdmin.SetPhoneChanged(TestUser.Customer, TestUser.Customer.Phone, "+79990001122", at);
        using HttpClient client = _factory.CreateClientFor(TestUser.Customer);

        using JsonDocument body = JsonDocument.Parse(await client.GetStringAsync(new Uri("/me/profile", UriKind.Relative)));

        JsonElement profile = body.RootElement.GetProperty("result");
        Assert.Equal("+79990001122", profile.GetProperty("phone").GetString());
        Assert.Equal(at, profile.GetProperty("phoneChangedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Profile_NoEmail_Null()
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.AdminInAdminMode);

        using JsonDocument body = JsonDocument.Parse(await client.GetStringAsync(new Uri("/me/profile", UriKind.Relative)));

        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("result").GetProperty("email").ValueKind);
        Assert.False(body.RootElement.GetProperty("result").GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task Profile_Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/me/profile", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
