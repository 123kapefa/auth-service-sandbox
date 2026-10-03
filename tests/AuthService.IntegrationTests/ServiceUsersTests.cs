using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>
/// API для сервисов проекта по ключу доступа: карточка пользователя и проверка доступа — только сервисам с правом;
/// человеку (даже админу) — 403; сервису туда, где нужен человек («я», смена режима, сессии) — 403.
/// </summary>
public sealed class ServiceUsersTests : IClassFixture<AuthServiceWebFactory>
{
    private const string ORDERS = "service-orders";
    private static readonly Guid _orders = Guid.NewGuid();

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public ServiceUsersTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        _keycloak.SeedAppRoles();
        factory.Revocations.Reset();
        _keycloak.Add(TestUser.Customer, ["customer"]);
        _keycloak.Add(TestUser.ManagerInManagerMode, ["customer", "manager"]);
        _keycloak.SetEmail(TestUser.Customer, "customer@sandbox.test");
    }

    [Fact]
    public async Task Service_ReadsUserById()
    {
        JsonElement user = await ResultAsync(await ServiceGetAsync($"/auth/services/users/{TestUser.Customer.Id}", AuthServicePermissions.ServiceAccess.USERS_READ));

        Assert.Equal(TestUser.Customer.Phone, user.GetProperty("phone").GetString());
        Assert.Equal("customer@sandbox.test", user.GetProperty("email").GetString());
        Assert.True(user.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Service_FindsUserByLogin()
    {
        JsonElement user = await ResultAsync(await ServiceGetAsync(
            $"/auth/services/users?login={TestUser.Customer.Phone.TrimStart('+')}", AuthServicePermissions.ServiceAccess.USERS_READ));

        Assert.Equal(TestUser.Customer.Id, user.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Service_UnknownUser_NotFound() =>
        Assert.Equal(HttpStatusCode.NotFound,
            (await ServiceGetAsync($"/auth/services/users/{Guid.NewGuid()}", AuthServicePermissions.ServiceAccess.USERS_READ)).StatusCode);

    [Fact]
    public async Task AccessCheck_PermissionOfActiveMode()
    {
        JsonElement allowed = await CheckAsync(TestUser.Customer, "profile.view");
        Assert.True(allowed.GetProperty("allowed").GetBoolean());
        Assert.Equal("customer", allowed.GetProperty("activeMode").GetString());

        JsonElement denied = await CheckAsync(TestUser.Customer, "users.view");
        Assert.False(denied.GetProperty("allowed").GetBoolean());
        Assert.Equal("permission.missing", denied.GetProperty("reason").GetString());

        Assert.True((await CheckAsync(TestUser.ManagerInManagerMode, "users.view")).GetProperty("allowed").GetBoolean());
    }

    [Fact]
    public async Task AccessCheck_BlockedUser_NotAllowed()
    {
        _keycloak.Add(TestUser.Customer, ["customer"], enabled: false);

        JsonElement result = await CheckAsync(TestUser.Customer, "profile.view");

        Assert.False(result.GetProperty("allowed").GetBoolean());
        Assert.Equal("user.blocked", result.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task AccessCheck_ClosedApp_NotAllowed()
    {
        _keycloak.SetAppRoles(TestUser.Customer, "manager-1c", "operator", AppRoles.ACCESS_DENIED);

        JsonElement result = await CheckAsync(TestUser.Customer, "orders.view", "manager-1c");

        Assert.False(result.GetProperty("allowed").GetBoolean());
        Assert.Equal("app.access_denied", result.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("/auth/services/users/{0}")]
    [InlineData("/auth/services/users/{0}/access?permission=profile.view")]
    public async Task Service_WithoutPermission_Forbidden(string path) =>
        Assert.Equal(HttpStatusCode.Forbidden, (await ServiceGetAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, TestUser.Customer.Id))).StatusCode);

    [Fact]
    public async Task Human_EvenAdmin_Forbidden()
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.AdminInAdminMode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri($"/auth/services/users/{TestUser.Customer.Id}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri($"/auth/services/users/{TestUser.Customer.Id}", UriKind.Relative))).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/me/")]
    [InlineData("GET", "/auth/admin/me")]
    [InlineData("GET", "/security/sessions")]
    [InlineData("POST", "/auth/mode")]
    public async Task Service_WhereHumanRequired_Forbidden(string method, string path)
    {
        using HttpClient client = _factory.CreateClientWithToken(_factory.CreateServiceToken(ORDERS, _orders, AuthServicePermissions.ServiceAccess.All.ToArray()));
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { mode = "customer" });
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Service_RevokedKey_Unauthorized()
    {
        string token = _factory.CreateServiceToken(ORDERS, _orders, AuthServicePermissions.ServiceAccess.USERS_READ);
        await _factory.Revocations.RevokeUserAsync(_orders.ToString(), DateTimeOffset.UtcNow.AddSeconds(5), default);

        using HttpClient client = _factory.CreateClientWithToken(token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri($"/auth/services/users/{TestUser.Customer.Id}", UriKind.Relative))).StatusCode);
    }

    private async Task<JsonElement> CheckAsync(TestUser user, string permission, string? app = null) =>
        await ResultAsync(await ServiceGetAsync(
            $"/auth/services/users/{user.Id}/access?permission={permission}" + (app is null ? string.Empty : $"&app={app}"),
            AuthServicePermissions.ServiceAccess.ACCESS_CHECK));

    private async Task<HttpResponseMessage> ServiceGetAsync(string path, params string[] permissions)
    {
        using HttpClient client = _factory.CreateClientWithToken(_factory.CreateServiceToken(ORDERS, _orders, permissions));
        return await client.GetAsync(new Uri(path, UriKind.Relative));
    }

    private static async Task<JsonElement> ResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("result").Clone();
    }
}
