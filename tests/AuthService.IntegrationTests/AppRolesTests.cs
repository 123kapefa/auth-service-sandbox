using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Features.Admin.Catalog;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Роли приложения: список с правами, создать, изменить состав, удалить — с подставным Admin API.</summary>
public sealed class AppRolesTests : IClassFixture<AuthServiceWebFactory>
{
    private const string ROLES = "/auth/admin/apps/sandbox-api/roles";

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public AppRolesTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        _factory.AuditLog.Reset();
        _factory.Services.GetServices<IHostedService>().OfType<PermissionCatalogSync>().Single().SyncOnceAsync(default).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task List_BuiltinRolesWithPermissions()
    {
        JsonElement roles = await ResultAsync(await SendAsync(HttpMethod.Get, ROLES, null));

        Assert.Equal(["admin", "customer", "manager"], Names(roles));
        JsonElement manager = Role(roles, "manager");
        Assert.True(manager.GetProperty("builtin").GetBoolean());
        Assert.Equal(["profile.view", "users.block", "users.view"], manager.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Create_WithPermissions_Listed_Audited()
    {
        JsonElement roles = await ResultAsync(await SendAsync(HttpMethod.Post, ROLES, new { name = "support", description = "Поддержка", permissions = new[] { "users.view", "profile.view" } }));

        JsonElement support = Role(roles, "support");
        Assert.False(support.GetProperty("builtin").GetBoolean());
        Assert.Equal(["profile.view", "users.view"], support.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Order(StringComparer.Ordinal));
        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal((AuditActions.ROLE_CREATED, AuditActions.ROLE_OBJECT, "support"), (entry.Action, entry.ObjectType, entry.ObjectName));
    }

    [Theory]
    [InlineData("Support", "invalid.format")]
    [InlineData("support.team", "invalid.format")]
    [InlineData("", "value.is.required")]
    public async Task Create_BadName(string name, string code)
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Post, ROLES, new { name, permissions = Array.Empty<string>() });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task Create_UnknownPermission_NothingCreated()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Post, ROLES, new { name = "support", permissions = new[] { "no.such" } });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "roles.unknown_permission");
        Assert.DoesNotContain(_keycloak.AppRolesOf("sandbox-api"), role => role.Name == "support");
    }

    [Fact]
    public async Task Create_Duplicate_Conflict()
    {
        await AssertErrorAsync(await SendAsync(HttpMethod.Post, ROLES, new { name = "manager" }), HttpStatusCode.Conflict, "roles.exists");
    }

    [Fact]
    public async Task Update_ExactPermissionList_AuditOldNew()
    {
        JsonElement roles = await ResultAsync(await SendAsync(HttpMethod.Put, $"{ROLES}/manager", new { description = "Менеджер", permissions = new[] { "profile.view", "audit.view" } }));

        Assert.Equal(["audit.view", "profile.view"], Role(roles, "manager").GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Order(StringComparer.Ordinal));
        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal(AuditActions.ROLE_UPDATED, entry.Action);
        using JsonDocument changes = JsonDocument.Parse(entry.Changes);
        Assert.Equal(["profile.view", "users.block", "users.view"], changes.RootElement.GetProperty("permissions").GetProperty("old").EnumerateArray().Select(p => p.GetString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Update_AdminWithoutRolesManage_Lockout()
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"{ROLES}/admin", new { permissions = new[] { "profile.view" } });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "roles.lockout");
        Assert.Contains("roles.manage", _keycloak.CompositesOf("sandbox-api", "admin"));
    }

    [Fact]
    public async Task Update_PermissionOrUnknown_Rejected()
    {
        await AssertErrorAsync(await SendAsync(HttpMethod.Put, $"{ROLES}/users.view", new { permissions = Array.Empty<string>() }), HttpStatusCode.BadRequest, "roles.not_role");
        await AssertErrorAsync(await SendAsync(HttpMethod.Put, $"{ROLES}/nobody", new { permissions = Array.Empty<string>() }), HttpStatusCode.NotFound, "roles.not_found");
        await AssertErrorAsync(await SendAsync(HttpMethod.Put, $"{ROLES}/manager", new { description = "без списка" }), HttpStatusCode.BadRequest, "value.is.required");
    }

    [Fact]
    public async Task Delete_Custom_Ok_Builtin_Conflict()
    {
        await SendAsync(HttpMethod.Post, ROLES, new { name = "support", permissions = new[] { "users.view" } });

        HttpResponseMessage custom = await SendAsync(HttpMethod.Delete, $"{ROLES}/support", null);
        HttpResponseMessage builtin = await SendAsync(HttpMethod.Delete, $"{ROLES}/manager", null);

        Assert.DoesNotContain(Names(await ResultAsync(custom)), name => name == "support");
        Assert.Equal(AuditActions.ROLE_DELETED, _factory.AuditLog.Entries[^1].Action);
        await AssertErrorAsync(builtin, HttpStatusCode.Conflict, "roles.builtin");
    }

    [Fact]
    public async Task Manager_Forbidden()
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.ManagerInManagerMode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri(ROLES, UriKind.Relative))).StatusCode);
    }

    private static string[] Names(JsonElement roles) => [.. roles.EnumerateArray().Select(role => role.GetProperty("name").GetString()!).Order(StringComparer.Ordinal)];

    private static JsonElement Role(JsonElement roles, string name) =>
        roles.EnumerateArray().Single(role => role.GetProperty("name").GetString() == name);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body)
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.AdminInAdminMode);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
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
