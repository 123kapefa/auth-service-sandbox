using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Доступ к приложениям: GET …/apps и PUT …/apps/{clientId}/access с подставным Admin API.</summary>
public sealed class AppAccessTests : IClassFixture<AuthServiceWebFactory>
{
    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public AppAccessTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        factory.Revocations.Reset();
        _keycloak.Add(TestUser.Customer, []);
        _keycloak.Add(TestUser.AdminInAdminMode, [AuthModes.ADMIN]);
        _keycloak.Add(TestUser.ManagerInManagerMode, [AuthModes.MANAGER]);
    }

    [Fact]
    public async Task List_AllAppsOpenByDefault()
    {
        HttpResponseMessage response = await GetAsync(TestUser.AdminInAdminMode, TestUser.Customer);

        Assert.Equal(["account-console:True", "manager-1c:True", "sandbox-bff:True"], await AppsAsync(response));
    }

    [Fact]
    public async Task Close_OneApp_OthersStayOpen()
    {
        HttpResponseMessage response = await PutAsync(TestUser.AdminInAdminMode, TestUser.Customer, "sandbox-bff", hasAccess: false);

        Assert.Equal(["account-console:True", "manager-1c:True", "sandbox-bff:False"], await AppsAsync(response));
        (Guid user, string[] grant, string[] revoke) = Assert.Single(_keycloak.RoleChanges);
        Assert.Equal(TestUser.Customer.Id, user);
        Assert.Equal(["access-denied"], grant);
        Assert.Empty(revoke);
        Assert.Empty(_keycloak.Logouts); // сессии остальных приложений не трогаем
    }

    [Fact]
    public async Task Reopen_RevokesRole()
    {
        await PutAsync(TestUser.AdminInAdminMode, TestUser.Customer, "sandbox-bff", hasAccess: false);

        HttpResponseMessage response = await PutAsync(TestUser.AdminInAdminMode, TestUser.Customer, "sandbox-bff", hasAccess: true);

        Assert.Equal(["account-console:True", "manager-1c:True", "sandbox-bff:True"], await AppsAsync(response));
        Assert.Equal(["access-denied"], _keycloak.RoleChanges[^1].Revoke);
    }

    [Fact]
    public async Task SameState_NothingChanged()
    {
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(TestUser.AdminInAdminMode, TestUser.Customer, "sandbox-bff", hasAccess: true)).StatusCode);
        Assert.Empty(_keycloak.RoleChanges);
    }

    [Fact]
    public async Task UnknownApp_NotFound()
    {
        await AssertErrorAsync(
            await PutAsync(TestUser.AdminInAdminMode, TestUser.Customer, "sandbox-api", hasAccess: false),
            HttpStatusCode.NotFound,
            "apps.not_found");
        Assert.Empty(_keycloak.RoleChanges);
    }

    [Fact]
    public async Task Manager_CustomerOk_StaffForbidden()
    {
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(TestUser.ManagerInManagerMode, TestUser.Customer, "sandbox-bff", false)).StatusCode);
        await AssertErrorAsync(
            await PutAsync(TestUser.ManagerInManagerMode, TestUser.AdminInAdminMode, "sandbox-bff", false),
            HttpStatusCode.Forbidden,
            "users.staff_forbidden");
        await AssertErrorAsync(await GetAsync(TestUser.ManagerInManagerMode, TestUser.AdminInAdminMode), HttpStatusCode.Forbidden, "users.staff_forbidden");
    }

    [Fact]
    public async Task Self_Forbidden()
    {
        await AssertErrorAsync(
            await PutAsync(TestUser.AdminInAdminMode, TestUser.AdminInAdminMode, "sandbox-bff", false),
            HttpStatusCode.Forbidden,
            "users.self_access");
    }

    [Fact]
    public async Task MissingFlag_ValidationError()
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.AdminInAdminMode);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/auth/admin/users/{TestUser.Customer.Id}/apps/sandbox-bff/access", UriKind.Relative), new { });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "value.is.required");
    }

    [Fact]
    public async Task Customer_Forbidden()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(TestUser.Customer, TestUser.Customer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(TestUser.Customer, TestUser.Customer, "sandbox-bff", false)).StatusCode);
    }

    private async Task<HttpResponseMessage> GetAsync(TestUser caller, TestUser target)
    {
        using HttpClient client = _factory.CreateClientFor(caller);
        return await client.GetAsync(new Uri($"/auth/admin/users/{target.Id}/apps", UriKind.Relative));
    }

    private async Task<HttpResponseMessage> PutAsync(TestUser caller, TestUser target, string clientId, bool hasAccess)
    {
        using HttpClient client = _factory.CreateClientFor(caller);
        return await client.PutAsJsonAsync(
            new Uri($"/auth/admin/users/{target.Id}/apps/{clientId}/access", UriKind.Relative), new { hasAccess });
    }

    private static async Task<string[]> AppsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return
        [
            .. body.RootElement.GetProperty("result").EnumerateArray()
                .Select(app => $"{app.GetProperty("clientId").GetString()}:{app.GetProperty("hasAccess").GetBoolean()}"),
        ];
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString());
    }
}
