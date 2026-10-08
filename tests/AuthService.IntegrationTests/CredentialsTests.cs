using System.Net;
using System.Text.Json;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Сброс пароля (письмо со ссылкой) и отключение 2FA — только админ проекта, с подставным Admin API.</summary>
public sealed class CredentialsTests : IClassFixture<AuthServiceWebFactory>
{
    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public CredentialsTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        factory.AuditLog.Reset();
        _keycloak.Add(TestUser.Customer, []);
        _keycloak.Add(TestUser.AdminInAdminMode, [AuthModes.ADMIN]);
        _keycloak.SetEmail(TestUser.Customer, "customer@sandbox.test");
    }

    [Fact]
    public async Task PasswordReset_EmailWithUpdatePassword_Audited()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/auth/admin/users/{TestUser.Customer.Id}/password-reset");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        (Guid user, string[] actions, TimeSpan lifespan) = Assert.Single(_keycloak.ActionEmails);
        Assert.Equal((TestUser.Customer.Id, TimeSpan.FromHours(24)), (user, lifespan));
        Assert.Equal(["UPDATE_PASSWORD"], actions);
        Assert.Equal(AuditActions.USER_PASSWORD_RESET_REQUESTED, Assert.Single(_factory.AuditLog.Entries).Action);
    }

    [Fact]
    public async Task PasswordReset_NoEmail_Conflict()
    {
        _keycloak.Add(TestUser.ManagerInManagerMode, [AuthModes.MANAGER]); // у тестовых пользователей почты нет

        await AssertErrorAsync(
            await SendAsync(HttpMethod.Post, $"/auth/admin/users/{TestUser.ManagerInManagerMode.Id}/password-reset"),
            HttpStatusCode.Conflict,
            "users.no_email");
        Assert.Empty(_keycloak.ActionEmails);
    }

    [Theory]
    [InlineData("POST", "password-reset")]
    [InlineData("DELETE", "two-factor")]
    public async Task Own_Forbidden(string method, string action) =>
        await AssertErrorAsync(
            await SendAsync(new HttpMethod(method), $"/auth/admin/users/{TestUser.AdminInAdminMode.Id}/{action}"),
            HttpStatusCode.Forbidden,
            "users.self_credentials");

    [Fact]
    public async Task Card_ShowsTwoFactor()
    {
        _keycloak.EnableTwoFactor(TestUser.Customer);

        Assert.True((await ResultAsync(await SendAsync(HttpMethod.Get, $"/auth/admin/users/{TestUser.Customer.Id}"))).GetProperty("hasTwoFactor").GetBoolean());
    }

    [Fact]
    public async Task DisableTwoFactor_Removed_Audited()
    {
        _keycloak.EnableTwoFactor(TestUser.Customer);

        JsonElement card = await ResultAsync(await SendAsync(HttpMethod.Delete, $"/auth/admin/users/{TestUser.Customer.Id}/two-factor"));

        Assert.False(card.GetProperty("hasTwoFactor").GetBoolean());
        Assert.Equal(AuditActions.USER_TWO_FACTOR_DISABLED, Assert.Single(_factory.AuditLog.Entries).Action);
    }

    [Fact]
    public async Task DisableTwoFactor_OldLogin_ReauthenticationRequired_StillEnabled()
    {
        _keycloak.EnableTwoFactor(TestUser.Customer);

        await AssertErrorAsync(
            await SendAsync(HttpMethod.Delete, $"/auth/admin/users/{TestUser.Customer.Id}/two-factor", loggedInAgo: TimeSpan.FromMinutes(10)),
            HttpStatusCode.Forbidden,
            "auth.reauthentication_required");
        Assert.True((await ResultAsync(await SendAsync(HttpMethod.Get, $"/auth/admin/users/{TestUser.Customer.Id}"))).GetProperty("hasTwoFactor").GetBoolean());
    }

    [Fact]
    public async Task DisableTwoFactor_NotEnabled_OkWithoutAudit()
    {
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Delete, $"/auth/admin/users/{TestUser.Customer.Id}/two-factor")).StatusCode);
        Assert.Empty(_factory.AuditLog.Entries);
    }

    [Theory]
    [MemberData(nameof(NotProjectAdmins))]
    public async Task NotProjectAdmin_Forbidden(TestUser caller)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Post, $"/auth/admin/users/{TestUser.Customer.Id}/password-reset", caller: caller)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Delete, $"/auth/admin/users/{TestUser.Customer.Id}/two-factor", caller: caller)).StatusCode);
        Assert.Empty(_keycloak.ActionEmails);
    }

    public static TheoryData<TestUser> NotProjectAdmins => new() { TestUser.ManagerInManagerMode, TestUser.AdminInCustomerMode, TestUser.Customer };

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, TimeSpan? loggedInAgo = null, TestUser? caller = null)
    {
        using HttpClient client = _factory.CreateClientFor(caller ?? TestUser.AdminInAdminMode, DateTimeOffset.UtcNow - (loggedInAgo ?? TimeSpan.Zero));
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
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
