using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>PUT /auth/admin/users/{id}/role с подставным Admin API.</summary>
public sealed class SetRoleTests : IClassFixture<AuthServiceWebFactory>
{
    // Второй админ в режиме admin — чтобы понижать не себя.
    private static readonly TestUser _otherAdmin =
        new("второй админ", Guid.NewGuid(), "+70000000002", [AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN);

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public SetRoleTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        factory.Revocations.Reset();
        _keycloak.Add(TestUser.Customer, []);
        _keycloak.Add(TestUser.AdminInAdminMode, [AuthModes.ADMIN]);
        _keycloak.Add(_otherAdmin, [AuthModes.ADMIN]);
    }

    [Fact]
    public async Task CustomerBecomesManager_AccessChanged_SessionsKept()
    {
        JsonElement card = await ResultAsync(await PutAsync(TestUser.Customer, "Manager"));

        Assert.Equal("manager", card.GetProperty("role").GetString());
        (Guid user, string[] grant, string[] revoke) = Assert.Single(_keycloak.RoleChanges);
        Assert.Equal(TestUser.Customer.Id, user);
        Assert.Equal(["manager"], grant);
        Assert.Empty(revoke);
        Assert.Empty(_keycloak.Logouts); // умный рефреш вместо выхода
        Assert.NotNull(_factory.Revocations.AccessVersion(TestUser.Customer.Id));
        Assert.Contains(_keycloak.AttributeChanges, change => change.UserId == TestUser.Customer.Id && change.Name == "access_version");
    }

    [Fact]
    public async Task GrantAdmin_AfterRecentLogin_Ok()
    {
        HttpResponseMessage response = await PutAsync(TestUser.Customer, "admin", loggedInAgo: TimeSpan.FromMinutes(1));

        Assert.Equal("admin", (await ResultAsync(response)).GetProperty("role").GetString());
    }

    [Fact]
    public async Task GrantAdmin_OldLogin_ReauthenticationRequired_NothingChanged()
    {
        HttpResponseMessage response = await PutAsync(TestUser.Customer, "admin", loggedInAgo: TimeSpan.FromMinutes(10));

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "auth.reauthentication_required");
        Assert.Empty(_keycloak.RoleChanges);
        Assert.Null(_factory.Revocations.AccessVersion(TestUser.Customer.Id));
    }

    [Fact]
    public async Task GrantManager_OldLogin_Ok()
    {
        HttpResponseMessage response = await PutAsync(TestUser.Customer, "manager", loggedInAgo: TimeSpan.FromHours(1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminDemotedToCustomer_ModeResetToCustomer()
    {
        JsonElement card = await ResultAsync(await PutAsync(_otherAdmin, "customer"));

        Assert.Equal("customer", card.GetProperty("role").GetString());
        Assert.Equal("customer", card.GetProperty("activeMode").GetString());
        (Guid user, string[] grant, string[] revoke) = Assert.Single(_keycloak.RoleChanges);
        Assert.Equal(_otherAdmin.Id, user);
        Assert.Empty(grant);
        Assert.Equal(["admin"], revoke);
        Assert.Contains((_otherAdmin.Id, "active_mode", "customer"), _keycloak.AttributeChanges);
        Assert.Empty(_keycloak.Logouts);
        Assert.NotNull(_factory.Revocations.AccessVersion(_otherAdmin.Id));
    }

    [Fact]
    public async Task SameRole_NothingChanged()
    {
        HttpResponseMessage response = await PutAsync(_otherAdmin, "admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_keycloak.RoleChanges);
        Assert.Null(_factory.Revocations.AccessVersion(_otherAdmin.Id));
    }

    [Fact]
    public async Task OwnRole_Forbidden()
    {
        await AssertErrorAsync(await PutAsync(TestUser.AdminInAdminMode, "customer"), HttpStatusCode.Forbidden, "users.self_role");
        Assert.Empty(_keycloak.RoleChanges);
    }

    [Theory]
    [InlineData("courier", "value.is.invalid")]
    [InlineData(" ", "value.is.required")]
    public async Task BadRole_ValidationError(string role, string code)
    {
        await AssertErrorAsync(await PutAsync(TestUser.Customer, role), HttpStatusCode.BadRequest, code);
        Assert.Empty(_keycloak.RoleChanges);
    }

    [Fact]
    public async Task UnknownUser_NotFound()
    {
        var ghost = new TestUser("нет такого", Guid.NewGuid(), "+70000000000", [], AuthModes.CUSTOMER);

        await AssertErrorAsync(await PutAsync(ghost, "manager"), HttpStatusCode.NotFound, "users.not_found");
    }

    [Theory]
    [MemberData(nameof(WithoutRolesManage))]
    public async Task WithoutRolesManage_Forbidden(TestUser caller)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(TestUser.Customer, "manager", caller: caller)).StatusCode);
        Assert.Empty(_keycloak.RoleChanges);
    }

    public static TheoryData<TestUser> WithoutRolesManage =>
        new() { TestUser.ManagerInManagerMode, TestUser.AdminInCustomerMode, TestUser.Customer };

    private async Task<HttpResponseMessage> PutAsync(TestUser target, string role, TimeSpan? loggedInAgo = null, TestUser? caller = null)
    {
        using HttpClient client = _factory.CreateClientFor(
            caller ?? TestUser.AdminInAdminMode,
            DateTimeOffset.UtcNow - (loggedInAgo ?? TimeSpan.Zero));
        return await client.PutAsJsonAsync(new Uri($"/auth/admin/users/{target.Id}/role", UriKind.Relative), new { role });
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
