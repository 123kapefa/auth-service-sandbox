using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Features.Admin.Catalog;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Роли пользователя в приложении: несколько ролей, роль = режим — с подставным Admin API.</summary>
public sealed class UserRolesTests : IClassFixture<AuthServiceWebFactory>
{
    private static readonly TestUser _staff =
        new("сотрудник", Guid.NewGuid(), "+70000000003", [AuthModes.CUSTOMER, AuthModes.MANAGER, "support"], "support");

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public UserRolesTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        factory.Revocations.Reset();
        factory.AuditLog.Reset();
        factory.Services.GetServices<IHostedService>().OfType<PermissionCatalogSync>().Single().SyncOnceAsync(default).GetAwaiter().GetResult();
        _keycloak.AddAppRole("sandbox-api", "support", "users.view", "profile.view");
        _keycloak.AddAppRole("sandbox-api", "auditor", "audit.view", "roles.manage");
        _keycloak.Add(TestUser.Customer, []);
        _keycloak.Add(TestUser.AdminInAdminMode, [AuthModes.ADMIN]);
        _keycloak.Add(_staff, [AuthModes.MANAGER, "support"]);
    }

    [Fact]
    public async Task Get_Customer_OnlyDefaultRole()
    {
        JsonElement roles = await ResultAsync(await SendAsync(HttpMethod.Get, TestUser.Customer));

        Assert.Empty(Strings(roles, "roles"));
        Assert.Equal(["customer"], Strings(roles, "defaultRoles"));
        Assert.Equal(["customer"], Strings(roles, "modes"));
        // роль по умолчанию не выдаётся; порядок — встроенные, затем свои по алфавиту
        Assert.Equal(
            ["manager", "admin", "auditor", "support"],
            [.. roles.GetProperty("assignableRoles").EnumerateArray().Select(role => role.GetProperty("name").GetString()!)]);
    }

    [Fact]
    public async Task Get_SeveralRoles_ModesInOrder()
    {
        JsonElement roles = await ResultAsync(await SendAsync(HttpMethod.Get, _staff));

        Assert.Equal(["manager", "support"], Strings(roles, "roles"));
        Assert.Equal(["customer", "manager", "support"], Strings(roles, "modes"));
        Assert.Equal("support", roles.GetProperty("activeMode").GetString());
    }

    [Fact]
    public async Task Set_SeveralRoles_Granted_AccessChanged_Audited()
    {
        JsonElement roles = await ResultAsync(await SendAsync(HttpMethod.Put, TestUser.Customer, new { roles = new[] { "Support", "manager" } }));

        Assert.Equal(["manager", "support"], Strings(roles, "roles"));
        (Guid user, string[] grant, string[] revoke) = Assert.Single(_keycloak.RoleChanges);
        Assert.Equal(TestUser.Customer.Id, user);
        Assert.Equal(["manager", "support"], grant);
        Assert.Empty(revoke);
        Assert.Empty(_keycloak.Logouts);
        Assert.NotNull(_factory.Revocations.AccessVersion(TestUser.Customer.Id));
        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal(AuditActions.USER_ROLE_CHANGED, entry.Action);
        using JsonDocument changes = JsonDocument.Parse(entry.Changes);
        Assert.Equal("sandbox-api", changes.RootElement.GetProperty("app").GetString());
        Assert.Equal("customer", changes.RootElement.GetProperty("role").GetProperty("old").GetString());
        Assert.Equal("manager,support", changes.RootElement.GetProperty("role").GetProperty("new").GetString());
    }

    [Fact]
    public async Task Set_RemoveActiveModeRole_ModeResetToDefault()
    {
        JsonElement roles = await ResultAsync(await SendAsync(HttpMethod.Put, _staff, new { roles = new[] { "manager" } }));

        Assert.Equal(["manager"], Strings(roles, "roles"));
        (_, string[] grant, string[] revoke) = Assert.Single(_keycloak.RoleChanges);
        Assert.Empty(grant);
        Assert.Equal(["support"], revoke);
        Assert.Contains((_staff.Id, "active_mode", "customer"), _keycloak.AttributeChanges);
    }

    [Fact]
    public async Task Set_DefaultRoleOnly_NothingChanged()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Put, TestUser.Customer, new { roles = new[] { "customer" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_keycloak.RoleChanges);
        Assert.Null(_factory.Revocations.AccessVersion(TestUser.Customer.Id));
        Assert.Empty(_factory.AuditLog.Entries);
    }

    [Theory]
    [InlineData("courier")]
    [InlineData("users.view")]
    [InlineData("access-denied")]
    public async Task Set_NotARole_BadRequest(string role)
    {
        await AssertErrorAsync(await SendAsync(HttpMethod.Put, TestUser.Customer, new { roles = new[] { "manager", role } }), HttpStatusCode.BadRequest, "roles.unknown_role");
        Assert.Empty(_keycloak.RoleChanges);
    }

    [Fact]
    public async Task Set_WithoutList_ValidationError() =>
        await AssertErrorAsync(await SendAsync(HttpMethod.Put, TestUser.Customer, new { }), HttpStatusCode.BadRequest, "value.is.required");

    [Theory]
    [InlineData("admin")]
    [InlineData("auditor")]
    public async Task Set_RoleThatManagesRoles_OldLogin_ReauthenticationRequired(string role)
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Put, TestUser.Customer, new { roles = new[] { role } }, loggedInAgo: TimeSpan.FromMinutes(10));

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "auth.reauthentication_required");
        Assert.Empty(_keycloak.RoleChanges);
    }

    [Fact]
    public async Task Set_OrdinaryRole_OldLogin_Ok()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Put, TestUser.Customer, new { roles = new[] { "support" } }, loggedInAgo: TimeSpan.FromHours(1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Set_Own_Forbidden()
    {
        await AssertErrorAsync(await SendAsync(HttpMethod.Put, TestUser.AdminInAdminMode, new { roles = Array.Empty<string>() }), HttpStatusCode.Forbidden, "users.self_role");
        Assert.Empty(_keycloak.RoleChanges);
    }

    [Fact]
    public async Task UnknownApp_NotFound() =>
        await AssertErrorAsync(await SendAsync(HttpMethod.Get, TestUser.Customer, app: "no-such-app"), HttpStatusCode.NotFound, "apps.not_found");

    [Fact]
    public async Task UnknownUser_NotFound()
    {
        var ghost = new TestUser("нет такого", Guid.NewGuid(), "+70000000000", [], AuthModes.CUSTOMER);

        await AssertErrorAsync(await SendAsync(HttpMethod.Get, ghost), HttpStatusCode.NotFound, "users.not_found");
    }

    [Fact]
    public async Task Manager_Forbidden()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, TestUser.Customer, caller: TestUser.ManagerInManagerMode)).StatusCode);
    }

    [Fact]
    public async Task SwitchMode_ToOwnCustomRole_Ok()
    {
        using HttpClient client = _factory.CreateClientFor(_staff);

        HttpResponseMessage response = await client.PostAsJsonAsync(new Uri("/auth/mode", UriKind.Relative), new { activeMode = "manager" });

        JsonElement result = await ResultAsync(response);
        Assert.Equal("manager", result.GetProperty("activeMode").GetString());
        Assert.Equal(["customer", "manager", "support"], Strings(result, "availableRoles"));
    }

    private static string[] Strings(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!)];

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        TestUser target,
        object? body = null,
        TimeSpan? loggedInAgo = null,
        TestUser? caller = null,
        string app = "sandbox-api")
    {
        using HttpClient client = _factory.CreateClientFor(
            caller ?? TestUser.AdminInAdminMode,
            DateTimeOffset.UtcNow - (loggedInAgo ?? TimeSpan.Zero));
        using var request = new HttpRequestMessage(method, new Uri($"/auth/admin/apps/{app}/users/{target.Id}/roles", UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("result").Clone();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString());
    }
}
