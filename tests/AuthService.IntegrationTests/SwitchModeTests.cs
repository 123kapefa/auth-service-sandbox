using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.Net.Http.Headers;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>POST /auth/mode с подставными сервером входа и Admin API.</summary>
public sealed class SwitchModeTests : IClassFixture<AuthServiceWebFactory>
{
    private readonly AuthServiceWebFactory _factory;

    public SwitchModeTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _factory.IdentityProvider.Reset();
        _factory.IdentityAdmin.Reset();
    }

    [Fact]
    public async Task AdminSwitchesToAdmin_ModeSaved_TokensRefreshedIntoCookies()
    {
        TestUser user = TestUser.AdminInCustomerMode;
        _factory.IdentityAdmin.Add(user, [AuthModes.ADMIN]);
        _factory.IdentityProvider.TokenResult = FakeIdentityProvider.Tokens("access-admin", "refresh-2");

        HttpResponseMessage response = await SwitchAsync(user, "admin", refreshCookie: "refresh-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((user.Id, "active_mode", "admin"), Assert.Single(_factory.IdentityAdmin.AttributeChanges));
        Assert.Equal("refresh-1", Assert.Single(_factory.IdentityProvider.Refreshes));
        Assert.Equal("access-admin", SetCookie(response, AuthCookies.ACCESS_TOKEN).Value.ToString());
        Assert.Equal("refresh-2", SetCookie(response, AuthCookies.REFRESH_TOKEN).Value.ToString());

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement result = body.RootElement.GetProperty("result");
        Assert.Equal(user.Id, result.GetProperty("userId").GetGuid());
        Assert.Equal("admin", result.GetProperty("activeMode").GetString());
        Assert.Equal("admin", result.GetProperty("role").GetString());
        Assert.Equal(["customer", "admin"], result.GetProperty("availableRoles").EnumerateArray().Select(mode => mode.GetString()));
        Assert.True(result.GetProperty("hasPassword").GetBoolean());
    }

    [Fact]
    public async Task ModeIsCaseInsensitive()
    {
        TestUser user = TestUser.ManagerInCustomerMode;
        _factory.IdentityAdmin.Add(user, [AuthModes.MANAGER]);

        HttpResponseMessage response = await SwitchAsync(user, " Manager ", refreshCookie: "refresh-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("manager", Assert.Single(_factory.IdentityAdmin.AttributeChanges).Value);
    }

    [Fact]
    public async Task WithoutRefreshCookie_ModeSaved_NoCookies()
    {
        TestUser user = TestUser.AdminInAdminMode;
        _factory.IdentityAdmin.Add(user, [AuthModes.ADMIN]);

        HttpResponseMessage response = await SwitchAsync(user, "customer", refreshCookie: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("customer", Assert.Single(_factory.IdentityAdmin.AttributeChanges).Value);
        Assert.Empty(_factory.IdentityProvider.Refreshes);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task CustomerToAdmin_Forbidden_NothingChanged()
    {
        TestUser user = TestUser.Customer;
        _factory.IdentityAdmin.Add(user, []);

        HttpResponseMessage response = await SwitchAsync(user, "admin", refreshCookie: "refresh-1");

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "auth.mode.not_available");
        Assert.Empty(_factory.IdentityAdmin.AttributeChanges);
        Assert.Empty(_factory.IdentityProvider.Refreshes);
    }

    [Fact]
    public async Task RoleRemovedInKeycloak_ButStillInToken_Forbidden()
    {
        // Токен ещё говорит «admin», а в Keycloak роль уже сняли: решают свежие роли.
        TestUser user = TestUser.AdminInCustomerMode;
        _factory.IdentityAdmin.Add(user, []);

        HttpResponseMessage response = await SwitchAsync(user, "admin", refreshCookie: "refresh-1");

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "auth.mode.not_available");
    }

    [Fact]
    public async Task UnknownMode_Forbidden()
    {
        TestUser user = TestUser.AdminInCustomerMode;
        _factory.IdentityAdmin.Add(user, [AuthModes.ADMIN]);

        HttpResponseMessage response = await SwitchAsync(user, "courier", refreshCookie: "refresh-1");

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "auth.mode.not_available");
    }

    [Fact]
    public async Task EmptyMode_ValidationError()
    {
        TestUser user = TestUser.Customer;
        _factory.IdentityAdmin.Add(user, []);

        HttpResponseMessage response = await SwitchAsync(user, " ", refreshCookie: "refresh-1");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "value.is.required");
    }

    [Fact]
    public async Task BlockedUser_Unauthorized_CookiesCleared()
    {
        TestUser user = TestUser.AdminInCustomerMode;
        _factory.IdentityAdmin.Add(user, [AuthModes.ADMIN], enabled: false);

        HttpResponseMessage response = await SwitchAsync(user, "admin", refreshCookie: "refresh-1");

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "auth.user.blocked");
        Assert.True(SetCookie(response, AuthCookies.ACCESS_TOKEN).Expires < DateTimeOffset.UtcNow);
        Assert.Empty(_factory.IdentityAdmin.AttributeChanges);
    }

    [Fact]
    public async Task Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClientFor(null);

        HttpResponseMessage response = await client.PostAsJsonAsync(new Uri("/auth/mode", UriKind.Relative), new { activeMode = "admin" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SwitchAsync(TestUser user, string mode, string? refreshCookie)
    {
        using HttpClient client = _factory.CreateClientFor(user);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/mode", UriKind.Relative))
        {
            Content = JsonContent.Create(new { activeMode = mode }),
        };
        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"{AuthCookies.REFRESH_TOKEN}={refreshCookie}");
        }

        return await client.SendAsync(request);
    }

    private static SetCookieHeaderValue SetCookie(HttpResponseMessage response, string name) =>
        Assert.Single(
            response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? SetCookieHeaderValue.ParseList([.. values]) : [],
            cookie => cookie.Name == name);

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString());
    }
}
