using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>PUT /auth/admin/users/{id}/restriction с подставным Admin API.</summary>
public sealed class RestrictionTests : IClassFixture<AuthServiceWebFactory>
{
    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public RestrictionTests(AuthServiceWebFactory factory)
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
    public async Task Admin_BlocksCustomer_WithReason_AndEndsSessions()
    {
        HttpResponseMessage response = await PutAsync(TestUser.AdminInAdminMode, TestUser.Customer, new { restricted = true, reason = " мошенничество " });

        JsonElement card = await ResultAsync(response);
        Assert.True(card.GetProperty("isRestricted").GetBoolean());
        Assert.Equal("мошенничество", card.GetProperty("restrictionReason").GetString());
        Assert.NotEqual(JsonValueKind.Null, card.GetProperty("restrictedAt").ValueKind);
        Assert.Equal(TestUser.Customer.Id, Assert.Single(_keycloak.Logouts));
    }

    [Fact]
    public async Task AlreadyBlocked_SilentSuccess_NothingChanged()
    {
        _keycloak.Add(TestUser.Customer, [], enabled: false);

        HttpResponseMessage response = await PutAsync(TestUser.AdminInAdminMode, TestUser.Customer, new { restricted = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_keycloak.AttributeChanges);
        Assert.Empty(_keycloak.Logouts);
    }

    [Fact]
    public async Task Unblock_EnablesAndClearsReason()
    {
        await PutAsync(TestUser.AdminInAdminMode, TestUser.Customer, new { restricted = true, reason = "проверка" });

        JsonElement card = await ResultAsync(await PutAsync(TestUser.AdminInAdminMode, TestUser.Customer, new { restricted = false }));

        Assert.False(card.GetProperty("isRestricted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, card.GetProperty("restrictionReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("restrictedAt").ValueKind);
    }

    [Fact]
    public async Task Manager_BlocksCustomer_ButNotStaff()
    {
        HttpResponseMessage customer = await PutAsync(TestUser.ManagerInManagerMode, TestUser.Customer, new { restricted = true });
        HttpResponseMessage staff = await PutAsync(TestUser.ManagerInManagerMode, TestUser.AdminInAdminMode, new { restricted = true });

        Assert.Equal(HttpStatusCode.OK, customer.StatusCode);
        await AssertErrorAsync(staff, HttpStatusCode.Forbidden, "users.staff_forbidden");
        Assert.DoesNotContain(TestUser.AdminInAdminMode.Id, _keycloak.Logouts);
    }

    [Fact]
    public async Task Self_Forbidden()
    {
        HttpResponseMessage response = await PutAsync(TestUser.AdminInAdminMode, TestUser.AdminInAdminMode, new { restricted = true });

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "users.self_restriction");
        Assert.Empty(_keycloak.Logouts);
    }

    [Fact]
    public async Task Validation_RestrictedRequired_ReasonUpTo500()
    {
        await AssertErrorAsync(
            await PutAsync(TestUser.AdminInAdminMode, TestUser.Customer, new { reason = "без флага" }),
            HttpStatusCode.BadRequest,
            "value.is.required");
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await PutAsync(TestUser.AdminInAdminMode, TestUser.Customer, new { restricted = true, reason = new string('x', 501) })).StatusCode);
        Assert.Empty(_keycloak.Logouts);
    }

    [Fact]
    public async Task UnknownUser_NotFound()
    {
        var ghost = new TestUser("нет такого", Guid.NewGuid(), "+70000000000", [], AuthModes.CUSTOMER);

        await AssertErrorAsync(
            await PutAsync(TestUser.AdminInAdminMode, ghost, new { restricted = true }),
            HttpStatusCode.NotFound,
            "users.not_found");
    }

    [Theory]
    [MemberData(nameof(WithoutUsersBlock))]
    public async Task WithoutUsersBlock_Forbidden(TestUser caller) =>
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await PutAsync(caller, TestUser.ManagerInManagerMode, new { restricted = true })).StatusCode);

    public static TheoryData<TestUser> WithoutUsersBlock => [TestUser.Customer, TestUser.AdminInCustomerMode];

    private async Task<HttpResponseMessage> PutAsync(TestUser caller, TestUser target, object body)
    {
        using HttpClient client = _factory.CreateClientFor(caller);
        return await client.PutAsJsonAsync(new Uri($"/auth/admin/users/{target.Id}/restriction", UriKind.Relative), body);
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
