using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.Core.Features.Admin.Catalog;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Каталог прав: системные из кода (сверка при старте) и свои из админки.</summary>
public sealed class PermissionCatalogTests : IClassFixture<AuthServiceWebFactory>
{
    private const string APP = "/auth/admin/apps/sandbox-api/permissions";

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public PermissionCatalogTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        _factory.AuditLog.Reset();
    }

    [Fact]
    public async Task Sync_CreatesCodePermissions_AsSystem()
    {
        await SyncAsync();

        AppRole[] permissions = [.. _keycloak.AppRolesOf("sandbox-api").Where(role => role.Kind == AppRoleKinds.PERMISSION)];
        Assert.Equal(
            AuthServicePermissions.Catalog.Select(permission => permission.Name).Order(StringComparer.Ordinal),
            permissions.Select(permission => permission.Name).Order(StringComparer.Ordinal));
        Assert.All(permissions, permission => Assert.Equal(AppRoleKinds.SOURCE_CODE, permission.Source));
        Assert.Equal("Свой профиль", permissions.Single(permission => permission.Name == AuthServicePermissions.Profiles.VIEW).Description);
    }

    [Fact]
    public async Task Sync_FixesDescription_KeepsOrphans()
    {
        await _keycloak.CreateAppRoleAsync("sandbox-api", new AppRole(null, AuthServicePermissions.Users.VIEW, "старое", AppRoleKinds.PERMISSION, AppRoleKinds.SOURCE_CODE, false), default);
        await _keycloak.CreateAppRoleAsync("sandbox-api", new AppRole(null, "legacy.thing", "убрали из кода", AppRoleKinds.PERMISSION, AppRoleKinds.SOURCE_CODE, false), default);

        await SyncAsync();

        Assert.Equal("Пользователи: список и карточка", _keycloak.AppRolesOf("sandbox-api").Single(role => role.Name == AuthServicePermissions.Users.VIEW).Description);
        Assert.Contains(_keycloak.AppRolesOf("sandbox-api"), role => role.Name == "legacy.thing");
    }

    [Fact]
    public async Task List_ShowsSourceAndOnlyPermissions()
    {
        await SyncAsync();

        JsonElement list = await ResultAsync(await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Get, APP, null));

        Assert.DoesNotContain(list.EnumerateArray(), item => item.GetProperty("name").GetString() == "manager");
        Assert.All(list.EnumerateArray(), item => Assert.Equal("code", item.GetProperty("source").GetString()));
    }

    [Fact]
    public async Task Create_Custom_Listed_Audited_DuplicateConflict()
    {
        await SyncAsync();

        JsonElement list = await ResultAsync(await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Post, APP, new { name = "reports.export", description = "Выгрузка отчётов" }));
        HttpResponseMessage duplicate = await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Post, APP, new { name = "reports.export" });

        JsonElement created = list.EnumerateArray().Single(item => item.GetProperty("name").GetString() == "reports.export");
        Assert.Equal("admin", created.GetProperty("source").GetString());
        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal((AuditActions.PERMISSION_CREATED, AuditActions.PERMISSION_OBJECT, "reports.export"), (entry.Action, entry.ObjectType, entry.ObjectName));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("roles.exists", await ErrorCodeAsync(duplicate));
    }

    [Theory]
    [InlineData("Reports.Export", "invalid.format")]
    [InlineData("reports", "invalid.format")]
    [InlineData("reports export", "invalid.format")]
    [InlineData("", "value.is.required")]
    public async Task Create_BadName_ValidationError(string name, string code)
    {
        HttpResponseMessage response = await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Post, APP, new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ErrorCodeAsync(response));
        Assert.Empty(_factory.AuditLog.Entries);
    }

    [Fact]
    public async Task Delete_Custom_Ok_System_Conflict_Role_BadRequest()
    {
        await SyncAsync();
        await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Post, APP, new { name = "reports.export" });

        HttpResponseMessage custom = await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Delete, $"{APP}/reports.export", null);
        HttpResponseMessage system = await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Delete, $"{APP}/users.view", null);
        HttpResponseMessage role = await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Delete, $"{APP}/manager", null);
        HttpResponseMessage unknown = await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Delete, $"{APP}/no.such", null);

        Assert.Equal(HttpStatusCode.OK, custom.StatusCode);
        Assert.DoesNotContain(_keycloak.AppRolesOf("sandbox-api"), item => item.Name == "reports.export");
        Assert.Equal(AuditActions.PERMISSION_DELETED, _factory.AuditLog.Entries[^1].Action);
        Assert.Equal("permissions.system", await ErrorCodeAsync(system));
        Assert.Equal(HttpStatusCode.Conflict, system.StatusCode);
        Assert.Equal("permissions.not_permission", await ErrorCodeAsync(role));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task NotAnApp_NotFound()
    {
        HttpResponseMessage response = await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Get, "/auth/admin/apps/sandbox-bff/permissions", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("apps.not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Manager_Forbidden() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(TestUser.ManagerInManagerMode, HttpMethod.Get, APP, null)).StatusCode);

    private Task SyncAsync() =>
        _factory.Services.GetServices<IHostedService>().OfType<PermissionCatalogSync>().Single().SyncOnceAsync(default);

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

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString();
    }
}
