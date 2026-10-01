using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Features.Admin.Catalog;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>
/// Умный рефреш: роли или состав роли изменились — старые токены сразу получают 401 access_changed (сессия жива),
/// новый токен с текущими версиями проходит, /auth/refresh отдаёт разницу доступа.
/// </summary>
public sealed class SmartRefreshTests : IClassFixture<AuthServiceWebFactory>
{
    private const string ROLES = "/auth/admin/apps/sandbox-api/roles";

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public SmartRefreshTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        factory.Revocations.Reset();
        factory.IdentityProvider.Reset();
        factory.AuditLog.Reset();
        factory.Services.GetServices<IHostedService>().OfType<PermissionCatalogSync>().Single().SyncOnceAsync(default).GetAwaiter().GetResult();
        _keycloak.Add(TestUser.Customer, []);
        _keycloak.Add(TestUser.AdminInAdminMode, [AuthModes.ADMIN]);
        _keycloak.Add(TestUser.ManagerInManagerMode, [AuthModes.MANAGER]);
    }

    [Fact]
    public async Task UserRolesChanged_OldToken_AccessChanged_NewVersionAccepted()
    {
        string old = _factory.CreateToken(TestUser.Customer);
        await PutUserRolesAsync(TestUser.Customer, "manager");
        long version = _factory.Revocations.AccessVersion(TestUser.Customer.Id)!.Value;

        await AssertAccessChangedAsync(await GetMeAsync(old));
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(Token(TestUser.Customer, (AuthClaims.ACCESS_VERSION, version)))).StatusCode);
        Assert.Contains(_keycloak.AttributeChanges, change => change == (TestUser.Customer.Id, "access_version", version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task OtherUsers_NotAffected()
    {
        await PutUserRolesAsync(TestUser.Customer, "manager");

        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(_factory.CreateToken(TestUser.ManagerInManagerMode))).StatusCode);
    }

    [Fact]
    public async Task RoleCompositionChanged_TokensInThatMode_AccessChanged_OtherModesNot()
    {
        string manager = _factory.CreateToken(TestUser.ManagerInManagerMode);
        string admin = _factory.CreateToken(TestUser.AdminInAdminMode);

        await SendAsAdminAsync(HttpMethod.Put, $"{ROLES}/manager", new { permissions = new[] { "profile.view", "users.view" } });
        long version = _factory.Revocations.ModeVersion("sandbox-api", "manager")!.Value;

        await AssertAccessChangedAsync(await GetMeAsync(manager));
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(admin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(Token(TestUser.ManagerInManagerMode, (AuthClaims.MODE_VERSION, version)))).StatusCode);
        Assert.Equal(version, _keycloak.AppRolesOf("sandbox-api").Single(role => role.Name == "manager").Version);
    }

    [Fact]
    public async Task OnlyDescriptionChanged_NoMark()
    {
        await SendAsAdminAsync(HttpMethod.Put, $"{ROLES}/manager", new { description = "Новое описание", permissions = new[] { "profile.view", "users.view", "users.block" } });

        Assert.Null(_factory.Revocations.ModeVersion("sandbox-api", "manager"));
    }

    [Fact]
    public async Task RoleDeleted_TokensInThatMode_AccessChanged()
    {
        _keycloak.AddAppRole("sandbox-api", "support", "users.view");

        await SendAsAdminAsync(HttpMethod.Delete, $"{ROLES}/support", null);

        Assert.NotNull(_factory.Revocations.ModeVersion("sandbox-api", "support"));
    }

    [Fact]
    public async Task RoleRecreated_AfterDelete_NewTokensAccepted()
    {
        _keycloak.AddAppRole("sandbox-api", "support", "users.view");
        await SendAsAdminAsync(HttpMethod.Delete, $"{ROLES}/support", null);
        long deleted = _factory.Revocations.ModeVersion("sandbox-api", "support")!.Value;
        await Task.Delay(5); // версии — миллисекунды: новая роль позже удаления

        await SendAsAdminAsync(HttpMethod.Post, ROLES, new { name = "support", permissions = new[] { "users.view" } });
        long? version = _keycloak.AppRolesOf("sandbox-api").Single(role => role.Name == "support").Version;

        Assert.NotNull(version);
        Assert.True(version > deleted);
        Assert.Equal(version, _factory.Revocations.ModeVersion("sandbox-api", "support"));
    }

    [Fact]
    public async Task PermissionDeleted_RolesThatHadIt_AccessChanged()
    {
        await SendAsAdminAsync(HttpMethod.Post, "/auth/admin/apps/sandbox-api/permissions", new { name = "reports.export" });
        _keycloak.AddAppRole("sandbox-api", "support", "users.view", "reports.export");

        await SendAsAdminAsync(HttpMethod.Delete, "/auth/admin/apps/sandbox-api/permissions/reports.export", null);

        Assert.NotNull(_factory.Revocations.ModeVersion("sandbox-api", "support"));
        Assert.Null(_factory.Revocations.ModeVersion("sandbox-api", "manager"));
    }

    [Fact]
    public async Task Revoked_WinsOverAccessChanged_Plain401()
    {
        string old = _factory.CreateToken(TestUser.Customer);
        await PutUserRolesAsync(TestUser.Customer, "manager");
        await _factory.Revocations.RevokeSessionAsync($"session-{TestUser.Customer.Id:N}", default);

        HttpResponseMessage response = await GetMeAsync(old);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("access_changed", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_ReturnsAccessChanges()
    {
        string old = _factory.CreateToken(TestUser.ManagerInManagerMode);
        var promoted = TestUser.ManagerInManagerMode with { Roles = [AuthModes.CUSTOMER, AuthModes.MANAGER, AuthModes.ADMIN], ActiveMode = AuthModes.ADMIN };
        _factory.IdentityProvider.TokenResult = FakeIdentityProvider.Tokens(_factory.CreateToken(promoted), "refresh-2");

        JsonElement changes = (await RefreshAsync(old)).GetProperty("changes");

        Assert.Equal(["admin"], Strings(changes, "addedRoles"));
        Assert.Empty(Strings(changes, "removedRoles"));
        Assert.Contains("roles.manage", Strings(changes, "addedPermissions"));
        Assert.Empty(Strings(changes, "removedPermissions"));
        Assert.Equal("admin", changes.GetProperty("activeMode").GetString());
    }

    [Fact]
    public async Task Refresh_Demoted_RemovedListed()
    {
        string old = _factory.CreateToken(TestUser.ManagerInManagerMode);
        var demoted = TestUser.ManagerInManagerMode with { Roles = [AuthModes.CUSTOMER], ActiveMode = AuthModes.CUSTOMER };
        _factory.IdentityProvider.TokenResult = FakeIdentityProvider.Tokens(_factory.CreateToken(demoted), "refresh-2");

        JsonElement changes = (await RefreshAsync(old)).GetProperty("changes");

        Assert.Equal(["manager"], Strings(changes, "removedRoles"));
        Assert.Equal(["users.block", "users.view"], Strings(changes, "removedPermissions"));
    }

    [Fact]
    public async Task Refresh_NothingChanged_ChangesNull()
    {
        string token = _factory.CreateToken(TestUser.ManagerInManagerMode);
        _factory.IdentityProvider.TokenResult = FakeIdentityProvider.Tokens(_factory.CreateToken(TestUser.ManagerInManagerMode), "refresh-2");

        Assert.Equal(JsonValueKind.Null, (await RefreshAsync(token)).GetProperty("changes").ValueKind);
    }

    private string Token(TestUser user, (string Name, long Value) version) =>
        _factory.CreateToken(user, extraClaims: new Dictionary<string, object>(StringComparer.Ordinal) { [version.Name] = version.Value });

    private async Task<HttpResponseMessage> GetMeAsync(string token)
    {
        using HttpClient client = _factory.CreateClientWithToken(token);
        return await client.GetAsync(new Uri("/me/", UriKind.Relative));
    }

    private async Task PutUserRolesAsync(TestUser user, params string[] roles) =>
        await SendAsAdminAsync(HttpMethod.Put, $"/auth/admin/apps/sandbox-api/users/{user.Id}/roles", new { roles });

    private async Task SendAsAdminAsync(HttpMethod method, string path, object? body)
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.AdminInAdminMode);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> RefreshAsync(string accessToken)
    {
        using HttpClient client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/refresh", UriKind.Relative));
        request.Headers.Add("Cookie", $"{AuthCookies.ACCESS_TOKEN}={accessToken}; {AuthCookies.REFRESH_TOKEN}=refresh-1");
        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("result").Clone();
    }

    private static async Task AssertAccessChangedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("error_description=\"access_changed\"", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("auth.access_changed", body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString());
    }

    private static string[] Strings(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!)];
}
