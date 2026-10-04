using System.Net;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>
/// Свои сессии (/security/sessions), история входов (/security/logins) и сессии пользователя в админке — с подставным
/// Admin API и устройствами сессий в памяти.
/// </summary>
public sealed class SessionsTests : IClassFixture<AuthServiceWebFactory>
{
    private const string CURRENT = "s-current";
    private const string MAC_CHROME = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";
    private const string IPHONE_SAFARI = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Mobile/15E148 Safari/604.1";

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;
    private readonly InMemoryLoginDevices _devices;

    public SessionsTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        factory.Revocations.Reset();
        _devices = factory.LoginDevices;
        _devices.Reset();
        _keycloak.Add(TestUser.Customer, []);
        _keycloak.Add(TestUser.AdminInAdminMode, [AuthModes.ADMIN]);
        _keycloak.Add(TestUser.ManagerInManagerMode, [AuthModes.MANAGER]);
        _keycloak.AddSession(TestUser.Customer, CURRENT, "sandbox-bff");
        _keycloak.AddSession(TestUser.Customer, "s-phone", "account-console", "sandbox-bff");
        _keycloak.AddSession(TestUser.Customer, "s-laptop", "sandbox-bff");
        _keycloak.AddSession(TestUser.AdminInAdminMode, "s-admin", "sandbox-bff");
    }

    [Fact]
    public async Task List_OwnSessions_CurrentMarked()
    {
        JsonElement result = await ResultAsync(await SendAsync(TestUser.Customer, HttpMethod.Get, "/security/sessions"));

        Assert.Equal(
            ["s-current:True", "s-laptop:False", "s-phone:False"],
            Items(result).Select(item => $"{item.GetProperty("id").GetString()}:{item.GetProperty("isCurrent").GetBoolean()}"));
        Assert.Equal(["account-console", "sandbox-bff"], Items(result).Single(item => item.GetProperty("id").GetString() == "s-phone")
            .GetProperty("apps").EnumerateArray().Select(app => app.GetString()));
    }

    [Fact]
    public async Task List_DeviceFromLogin_UnknownWithoutRecord()
    {
        _devices.Add(TestUser.Customer, CURRENT, MAC_CHROME);
        _devices.Add(TestUser.Customer, "s-phone", IPHONE_SAFARI);

        JsonElement result = await ResultAsync(await SendAsync(TestUser.Customer, HttpMethod.Get, "/security/sessions"));

        Assert.Equal(
            ["s-current:Chrome · macOS", "s-laptop:", "s-phone:Safari · iPhone"],
            Items(result).Select(item => $"{item.GetProperty("id").GetString()}:{item.GetProperty("deviceLabel").GetString()}"));
        Assert.Equal(MAC_CHROME, Items(result).First().GetProperty("userAgent").GetString());
    }

    [Fact]
    public async Task List_DevicesUnavailable_SessionsStillShown()
    {
        _devices.Broken = true;

        JsonElement result = await ResultAsync(await SendAsync(TestUser.Customer, HttpMethod.Get, "/security/sessions"));

        Assert.Equal(3, Items(result).Count());
        Assert.All(Items(result), item => Assert.Equal(JsonValueKind.Null, item.GetProperty("deviceLabel").ValueKind));
    }

    [Fact]
    public async Task Logins_Own_WithDeviceAndReason_NewestFirst()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _devices.Add(TestUser.Customer, "s-phone", IPHONE_SAFARI);
        _devices.Add(TestUser.AdminInAdminMode, "s-admin", MAC_CHROME);
        _keycloak.LoginEvents.Add(new LoginEvent(
            now, LoginEventTypes.LOGIN, TestUser.Customer.Id, "10.0.0.1", "sandbox-bff", null, "79990000001", "s-phone"));
        _keycloak.LoginEvents.Add(new LoginEvent(
            now.AddMinutes(-1), LoginEventTypes.LOGIN_ERROR, TestUser.Customer.Id, "10.0.0.2", "sandbox-bff", "invalid_user_credentials", "79990000001"));
        _keycloak.LoginEvents.Add(new LoginEvent(
            now.AddMinutes(-2), LoginEventTypes.LOGOUT, TestUser.Customer.Id, "10.0.0.1", "sandbox-bff", null, "79990000001", "s-old"));
        _keycloak.LoginEvents.Add(new LoginEvent(
            now.AddMinutes(-3), LoginEventTypes.LOGIN, TestUser.AdminInAdminMode.Id, "10.0.0.9", "sandbox-bff", null, "71234567890", "s-admin"));

        JsonElement result = await ResultAsync(await SendAsync(TestUser.Customer, HttpMethod.Get, "/security/logins"));

        JsonElement[] items = [.. Items(result)];
        Assert.Equal(2, items.Length); // выход и чужой вход не показываются
        Assert.True(items[0].GetProperty("success").GetBoolean());
        Assert.Equal("Safari · iPhone", items[0].GetProperty("deviceLabel").GetString());
        Assert.Equal("10.0.0.1", items[0].GetProperty("ipAddress").GetString());
        Assert.False(items[1].GetProperty("success").GetBoolean());
        Assert.Equal("invalid_user_credentials", items[1].GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("deviceLabel").ValueKind);
    }

    [Fact]
    public async Task Logins_Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/security/logins", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Terminate_OwnSession()
    {
        JsonElement result = await ResultAsync(await SendAsync(TestUser.Customer, HttpMethod.Post, "/security/sessions/s-phone/terminate"));

        Assert.Equal("s-phone", Assert.Single(_keycloak.DeletedSessions));
        Assert.DoesNotContain(Items(result), item => item.GetProperty("id").GetString() == "s-phone");
    }

    [Fact]
    public async Task Terminate_ForeignSession_NotFound()
    {
        HttpResponseMessage response = await SendAsync(TestUser.Customer, HttpMethod.Post, "/security/sessions/s-admin/terminate");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("sessions.not_found", await ErrorCodeAsync(response));
        Assert.Empty(_keycloak.DeletedSessions);
    }

    [Fact]
    public async Task TerminateAllOthers_KeepsCurrent()
    {
        JsonElement result = await ResultAsync(await SendAsync(TestUser.Customer, HttpMethod.Post, "/security/sessions/terminate-all-others"));

        Assert.Equal(2, result.GetProperty("terminated").GetInt32());
        Assert.Equal(["s-laptop", "s-phone"], _keycloak.DeletedSessions.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/security/sessions", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Admin_SeesUserSessions_NoCurrentMark()
    {
        JsonElement result = await ResultAsync(
            await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Get, $"/auth/admin/users/{TestUser.Customer.Id}/sessions"));

        Assert.Equal(3, Items(result).Count());
        Assert.DoesNotContain(Items(result), item => item.GetProperty("isCurrent").GetBoolean());
    }

    [Fact]
    public async Task Manager_SeesCustomerSessions_NotStaff()
    {
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsync(TestUser.ManagerInManagerMode, HttpMethod.Get, $"/auth/admin/users/{TestUser.Customer.Id}/sessions")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsync(TestUser.ManagerInManagerMode, HttpMethod.Get, $"/auth/admin/users/{TestUser.AdminInAdminMode.Id}/sessions")).StatusCode);
    }

    [Fact]
    public async Task Admin_LogoutEverywhere()
    {
        JsonElement result = await ResultAsync(
            await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Post, $"/auth/admin/users/{TestUser.Customer.Id}/logout"));

        Assert.Equal(TestUser.Customer.Id, Assert.Single(_keycloak.Logouts));
        Assert.Empty(Items(result));
    }

    [Fact]
    public async Task Manager_CannotLogoutEverywhere()
    {
        HttpResponseMessage response = await SendAsync(
            TestUser.ManagerInManagerMode, HttpMethod.Post, $"/auth/admin/users/{TestUser.Customer.Id}/logout");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_keycloak.Logouts);
    }

    private static IEnumerable<JsonElement> Items(JsonElement result) => result.GetProperty("items").EnumerateArray();

    private async Task<HttpResponseMessage> SendAsync(TestUser user, HttpMethod method, string path)
    {
        using HttpClient client = _factory.CreateClientFor(user, sessionId: CURRENT);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("result").Clone();
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString();
    }
}
