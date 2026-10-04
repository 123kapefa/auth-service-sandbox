using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Журнал: действия сотрудников пишутся и читаются; входы — из событий Keycloak.</summary>
public sealed class JournalTests : IClassFixture<AuthServiceWebFactory>
{
    private readonly AuthServiceWebFactory _factory;

    public JournalTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _factory.AuditLog.Reset();
        _factory.Revocations.Reset();
        _factory.IdentityAdmin.Reset();
        _factory.IdentityAdmin.Add(TestUser.Customer, []);
        _factory.IdentityAdmin.Add(TestUser.AdminInAdminMode, [AuthModes.ADMIN]);
        _factory.IdentityAdmin.Add(TestUser.ManagerInManagerMode, [AuthModes.MANAGER]);
    }

    [Fact]
    public async Task Block_RecordedWithActorReasonAndChange()
    {
        await SendAsync(TestUser.ManagerInManagerMode, HttpMethod.Put, $"/auth/admin/users/{TestUser.Customer.Id}/restriction",
            new { restricted = true, reason = "мошенничество" });

        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal(AuditActions.USER_RESTRICTION_CHANGED, entry.Action);
        Assert.Equal(TestUser.Customer.Id, entry.ObjectId);
        Assert.Equal("79990000001", entry.ObjectName);
        Assert.Equal(TestUser.ManagerInManagerMode.Id, entry.ActorId);
        Assert.Equal("+70987654321", entry.ActorName);
        Assert.Equal("manager", entry.ActorRole);
        using JsonDocument changes = JsonDocument.Parse(entry.Changes);
        Assert.False(changes.RootElement.GetProperty("restricted").GetProperty("old").GetBoolean());
        Assert.True(changes.RootElement.GetProperty("restricted").GetProperty("new").GetBoolean());
        Assert.Equal("мошенничество", changes.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RepeatedBlock_NoChange_NoRecord()
    {
        _factory.IdentityAdmin.Add(TestUser.Customer, [], enabled: false);

        await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Put, $"/auth/admin/users/{TestUser.Customer.Id}/restriction", new { restricted = true });

        Assert.Empty(_factory.AuditLog.Entries);
    }

    [Fact]
    public async Task RoleAppAccessAndLogout_Recorded()
    {
        string user = $"/auth/admin/users/{TestUser.Customer.Id}";
        await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Put, $"{user}/role", new { role = "manager" });
        await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Put, $"{user}/apps/sandbox-bff/access", new { hasAccess = false });
        await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Post, $"{user}/logout", null);

        Assert.Equal(
            [AuditActions.USER_ROLE_CHANGED, AuditActions.USER_APP_ACCESS_CHANGED, AuditActions.USER_LOGGED_OUT_EVERYWHERE],
            _factory.AuditLog.Entries.Select(entry => entry.Action));
        using JsonDocument role = JsonDocument.Parse(_factory.AuditLog.Entries[0].Changes);
        Assert.Equal("customer", role.RootElement.GetProperty("role").GetProperty("old").GetString());
        Assert.Equal("manager", role.RootElement.GetProperty("role").GetProperty("new").GetString());
    }

    [Fact]
    public async Task AuditDown_ActionStillDone()
    {
        _factory.AuditLog.Broken = true;

        HttpResponseMessage response = await SendAsync(
            TestUser.AdminInAdminMode, HttpMethod.Put, $"/auth/admin/users/{TestUser.Customer.Id}/restriction", new { restricted = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestUser.Customer.Id, Assert.Single(_factory.IdentityAdmin.Logouts));
    }

    [Fact]
    public async Task ReadAudit_FilterByObject_ChangesAsJson()
    {
        await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Put, $"/auth/admin/users/{TestUser.Customer.Id}/restriction", new { restricted = true });
        await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Put, $"/auth/admin/users/{TestUser.ManagerInManagerMode.Id}/role", new { role = "customer" });

        JsonElement page = await ResultAsync(await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Get, $"/auth/admin/audit?objectId={TestUser.Customer.Id}", null));

        JsonElement item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(AuditActions.USER_RESTRICTION_CHANGED, item.GetProperty("action").GetString());
        Assert.True(item.GetProperty("changes").GetProperty("restricted").GetProperty("new").GetBoolean());
    }

    [Fact]
    public async Task ReadAudit_OnlyAuditView()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(TestUser.ManagerInManagerMode, HttpMethod.Get, "/auth/admin/audit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(TestUser.ManagerInManagerMode, HttpMethod.Get, "/auth/admin/login-events", null)).StatusCode);
    }

    [Fact]
    public async Task ReadAudit_BadPeriod_ValidationError()
    {
        HttpResponseMessage response = await SendAsync(
            TestUser.AdminInAdminMode, HttpMethod.Get, "/auth/admin/audit?from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginEvents_ForUser_AndLastLoginInCard()
    {
        DateTimeOffset lastLogin = DateTimeOffset.UtcNow.AddMinutes(-3);
        _factory.IdentityAdmin.LoginEvents.Add(
            new LoginEvent(lastLogin, LoginEventTypes.LOGIN, TestUser.Customer.Id, "10.0.0.1", "sandbox-bff", null, "79990000001"));
        _factory.IdentityAdmin.LoginEvents.Add(new LoginEvent(
            lastLogin.AddMinutes(-1), LoginEventTypes.LOGIN_ERROR, TestUser.Customer.Id, "10.0.0.1", "sandbox-bff", "invalid_user_credentials", "79990000001"));
        _factory.IdentityAdmin.LoginEvents.Add(new LoginEvent(
            lastLogin.AddMinutes(-2), LoginEventTypes.LOGIN, TestUser.AdminInAdminMode.Id, "10.0.0.2", "sandbox-bff", null, "71234567890"));

        JsonElement events = await ResultAsync(await SendAsync(
            TestUser.AdminInAdminMode, HttpMethod.Get, $"/auth/admin/login-events?userId={TestUser.Customer.Id}", null));
        JsonElement card = await ResultAsync(await SendAsync(
            TestUser.AdminInAdminMode, HttpMethod.Get, $"/auth/admin/users/{TestUser.Customer.Id}", null));

        Assert.Equal(["LOGIN", "LOGIN_ERROR"], events.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("type").GetString()));
        Assert.Equal(lastLogin.ToUnixTimeSeconds(), card.GetProperty("lastLoginAt").GetDateTimeOffset().ToUnixTimeSeconds());
    }

    private async Task<HttpResponseMessage> SendAsync(TestUser caller, HttpMethod method, string path, object? body)
    {
        using HttpClient client = _factory.CreateClientFor(caller);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("result").Clone();
    }
}
