using System.Net;
using System.Text.Json;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Свои сессии (/security/sessions) и сессии пользователя в админке — с подставным Admin API.</summary>
public sealed class SessionsTests : IClassFixture<AuthServiceWebFactory>
{
    private const string CURRENT = "s-current";

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public SessionsTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
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
