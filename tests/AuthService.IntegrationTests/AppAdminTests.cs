using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>
/// Второе приложение «Менеджер 1С» и его админ: управляет ролями и правами только своего приложения — по правам своей
/// роли в нём; к приложению сервиса и к аккаунтам (блокировка, сессии) доступа нет.
/// </summary>
public sealed class AppAdminTests : IClassFixture<AuthServiceWebFactory>
{
    private const string APP = "/auth/admin/apps/manager-1c";

    // В sandbox-api — обычный покупатель, в «Менеджере 1С» — admin.
    private static readonly TestUser _appAdmin = new("админ 1С", Guid.NewGuid(), "+79990000007", [AuthModes.CUSTOMER], AuthModes.CUSTOMER);
    // Админ приложения с урезанной ролью: может выдавать роли (users.roles.manage), но своих прав — только orders.view.
    private static readonly TestUser _lead = new("старший 1С", Guid.NewGuid(), "+79990000009", [AuthModes.CUSTOMER], AuthModes.CUSTOMER);
    private static readonly TestUser _operator = new("оператор 1С", Guid.NewGuid(), "+79990000008", [AuthModes.CUSTOMER], AuthModes.CUSTOMER);

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public AppAdminTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        factory.Revocations.Reset();
        factory.AuditLog.Reset();
        _keycloak.Add(TestUser.AdminInAdminMode, [AuthModes.ADMIN]);
        _keycloak.Add(TestUser.AdminInCustomerMode, [AuthModes.ADMIN]); // роль admin есть, режим — customer
        _keycloak.Add(TestUser.Customer, []);
        _keycloak.Add(_appAdmin, []);
        _keycloak.SetAppRoles(_appAdmin, FakeIdentityAdmin.MANAGER_1C, "admin");
        _keycloak.Add(_operator, []);
        _keycloak.SetAppRoles(_operator, FakeIdentityAdmin.MANAGER_1C, "operator");
        _keycloak.UpdateUserAsync(_operator.Id, new(null, new Dictionary<string, string?>(StringComparer.Ordinal) { ["active_mode.manager-1c"] = "operator" }), default).GetAwaiter().GetResult();
        _keycloak.AttributeChanges.Clear();
    }

    [Fact]
    public async Task AppAdmin_SeesOwnAppRoles()
    {
        JsonElement roles = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Get, $"{APP}/roles"));

        Assert.Equal(["accountant", "admin", "operator"], [.. roles.EnumerateArray().Select(role => role.GetProperty("name").GetString()!).Order(StringComparer.Ordinal)]);
    }

    [Theory]
    [InlineData("/auth/admin/apps/sandbox-api/roles")]
    [InlineData("/auth/admin/apps/sandbox-api/permissions")]
    [InlineData("/auth/admin/apps/sandbox-api/users")]
    public async Task AppAdmin_ServiceApp_Forbidden(string path) =>
        await AssertErrorAsync(await SendAsync(_appAdmin, HttpMethod.Get, path), HttpStatusCode.Forbidden, "apps.forbidden");

    [Theory]
    [InlineData("/auth/admin/users")]
    [InlineData("/auth/admin/audit")]
    public async Task AppAdmin_ProjectAdmin_Forbidden(string path) =>
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_appAdmin, HttpMethod.Get, path)).StatusCode);

    [Fact]
    public async Task AppAdmin_BlockAccount_Forbidden() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_appAdmin, HttpMethod.Put, $"/auth/admin/users/{_operator.Id}/restriction", new { restricted = true })).StatusCode);

    [Fact]
    public async Task AppRoleWithoutRolesManage_Forbidden() =>
        await AssertErrorAsync(await SendAsync(_operator, HttpMethod.Get, $"{APP}/roles"), HttpStatusCode.Forbidden, "apps.forbidden");

    [Fact]
    public async Task ProjectAdmin_InCustomerMode_ServiceApp_Forbidden()
    {
        // Роль admin в sandbox-api есть, но режим — customer: в приложении сервиса действуют только права режима.
        await AssertErrorAsync(await SendAsync(TestUser.AdminInCustomerMode, HttpMethod.Get, "/auth/admin/apps/sandbox-api/roles"), HttpStatusCode.Forbidden, "apps.forbidden");
    }

    [Fact]
    public async Task ProjectAdmin_ManagesAnyApp() =>
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Get, $"{APP}/roles")).StatusCode);

    [Fact]
    public async Task AppUsers_SearchWithAppRoles()
    {
        JsonElement page = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Get, $"{APP}/users?search=79990000008"));

        JsonElement user = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(_operator.Id, user.GetProperty("id").GetGuid());
        Assert.Equal(["operator"], Strings(user, "roles"));
    }

    [Fact]
    public async Task AppUsers_NoSearch_OnlyWithRoleInApp()
    {
        JsonElement page = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Get, $"{APP}/users"));

        Guid[] ids = [.. page.GetProperty("items").EnumerateArray().Select(user => user.GetProperty("id").GetGuid())];
        Assert.Equal(new[] { _appAdmin.Id, _operator.Id }.Order(), ids.Order());
    }

    [Fact]
    public async Task AppUsers_ExactPhone_FindsUserWithoutRole()
    {
        string phone = Uri.EscapeDataString(TestUser.Customer.Phone);

        JsonElement page = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Get, $"{APP}/users?search={phone}"));

        JsonElement user = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(TestUser.Customer.Id, user.GetProperty("id").GetGuid());
        Assert.Empty(Strings(user, "roles"));
    }

    [Fact]
    public async Task AppUsers_PartialSearch_NothingFound()
    {
        JsonElement page = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Get, $"{APP}/users?search=7999"));

        Assert.Empty(page.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task AdminMe_AppAdmin_OnlyOwnApp()
    {
        JsonElement me = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Get, "/auth/admin/me"));

        JsonElement app = Assert.Single(me.GetProperty("apps").EnumerateArray());
        Assert.Equal("manager-1c", app.GetProperty("clientId").GetString());
        Assert.False(app.GetProperty("viaProject").GetBoolean());
        Assert.Equal(["audit.view", "roles.manage", "users.block", "users.roles.manage"], [.. Strings(app, "permissions").Order(StringComparer.Ordinal)]);
        Assert.Equal(["profile.view"], Strings(me, "permissions"));
    }

    [Fact]
    public async Task AdminMe_ProjectAdmin_AllAppsWithRoles()
    {
        JsonElement me = await ResultAsync(await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Get, "/auth/admin/me"));

        Assert.Equal(["manager-1c", "sandbox-api"], [.. me.GetProperty("apps").EnumerateArray().Select(app => app.GetProperty("clientId").GetString()!)]);
        Assert.All(me.GetProperty("apps").EnumerateArray(), app => Assert.True(app.GetProperty("viaProject").GetBoolean()));
        Assert.Contains("audit.view", Strings(me, "permissions"));
    }

    [Theory]
    [MemberData(nameof(NotAdmins))]
    public async Task AdminMe_NotAdmin_NoApps(TestUser caller)
    {
        JsonElement me = await ResultAsync(await SendAsync(caller, HttpMethod.Get, "/auth/admin/me"));

        Assert.Empty(me.GetProperty("apps").EnumerateArray());
    }

    public static TheoryData<TestUser> NotAdmins => new() { TestUser.Customer, _operator, TestUser.AdminInCustomerMode };

    [Fact]
    public async Task AdminMe_Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/auth/admin/me", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task UserRoles_InApp_ModeFromAppAttribute()
    {
        JsonElement roles = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Get, $"{APP}/users/{_operator.Id}/roles"));

        Assert.Equal(["operator"], Strings(roles, "roles"));
        Assert.Empty(Strings(roles, "defaultRoles"));
        Assert.Equal("operator", roles.GetProperty("activeMode").GetString());
        Assert.Equal(
            ["admin", "accountant", "operator"],
            [.. roles.GetProperty("assignableRoles").EnumerateArray().Select(role => role.GetProperty("name").GetString()!)]);
    }

    [Fact]
    public async Task AppAdmin_ChangesRoles_ModeInAppMovesToRemainingRole_AccessChanged()
    {
        JsonElement roles = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Put, $"{APP}/users/{_operator.Id}/roles", new { roles = new[] { "accountant" } }));

        Assert.Equal(["accountant"], Strings(roles, "roles"));
        Assert.Contains((_operator.Id, "active_mode.manager-1c", "accountant"), _keycloak.AttributeChanges);
        Assert.DoesNotContain(_keycloak.AttributeChanges, change => change.Name == "active_mode");
        Assert.NotNull(_factory.Revocations.AccessVersion(_operator.Id));
        Assert.Equal(["accountant"], [.. Assert.Single(_keycloak.RoleChanges).Grant]);
    }

    [Fact]
    public async Task FirstRoleInApp_BecomesMode()
    {
        await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Put, $"{APP}/users/{TestUser.Customer.Id}/roles", new { roles = new[] { "operator" } }));

        Assert.Contains((TestUser.Customer.Id, "active_mode.manager-1c", "operator"), _keycloak.AttributeChanges);
    }

    [Fact]
    public async Task AppAdmin_GrantsAppAdmin_OldLogin_ReauthenticationRequired() =>
        await AssertErrorAsync(
            await SendAsync(_appAdmin, HttpMethod.Put, $"{APP}/users/{_operator.Id}/roles", new { roles = new[] { "admin" } }, loggedInAgo: TimeSpan.FromMinutes(10)),
            HttpStatusCode.Forbidden,
            "auth.reauthentication_required");

    [Fact]
    public async Task AppAdmin_CannotChangeServiceAppRoles() =>
        await AssertErrorAsync(
            await SendAsync(_appAdmin, HttpMethod.Put, $"/auth/admin/apps/sandbox-api/users/{_operator.Id}/roles", new { roles = new[] { "manager" } }),
            HttpStatusCode.Forbidden,
            "apps.forbidden");

    [Fact]
    public async Task AppAdmin_CreatesOwnPermissionAndRole()
    {
        await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Post, $"{APP}/permissions", new { name = "orders.export", description = "Выгрузка заказов" }));
        JsonElement roles = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Post, $"{APP}/roles", new { name = "logist", permissions = new[] { "orders.view", "orders.export" } }));

        JsonElement logist = roles.EnumerateArray().Single(role => role.GetProperty("name").GetString() == "logist");
        Assert.Equal(["orders.export", "orders.view"], [.. Strings(logist, "permissions").Order(StringComparer.Ordinal)]);
        Assert.Equal(2, _factory.AuditLog.Entries.Count);
    }

    [Fact]
    public async Task AppAdmin_ClosesAccessToOwnApp_SeesOnlyIt_TokensRevoked()
    {
        JsonElement apps = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Put, $"/auth/admin/users/{_operator.Id}/apps/manager-1c/access", new { hasAccess = false }));

        JsonElement app = Assert.Single(apps.EnumerateArray());
        Assert.Equal("manager-1c", app.GetProperty("clientId").GetString());
        Assert.False(app.GetProperty("hasAccess").GetBoolean());
        Assert.True(_factory.Revocations.HasApp("manager-1c", _operator.Id));
        JsonElement roles = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Get, $"{APP}/users/{_operator.Id}/roles"));
        Assert.False(roles.GetProperty("hasAccess").GetBoolean());
        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal(("UserAppAccessChanged", "manager-1c"), (entry.Action, entry.App));
    }

    [Fact]
    public async Task AppClosedByRolesClient_TokenOfAnyLoginClient_Revoked()
    {
        // Сервис приложения проверяет свои токены по audience (клиенту ролей): токен любого клиента входа отозван.
        string token = _factory.CreateToken(TestUser.Customer, authorizedParty: "some-login-client");
        await _factory.Revocations.RevokeAppAsync("sandbox-api", TestUser.Customer.Id.ToString(), DateTimeOffset.UtcNow, default);

        using HttpClient client = _factory.CreateClientWithToken(token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/me/", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task AppAdmin_OtherAppAccess_Forbidden() =>
        await AssertErrorAsync(
            await SendAsync(_appAdmin, HttpMethod.Put, $"/auth/admin/users/{_operator.Id}/apps/sandbox-bff/access", new { hasAccess = false }),
            HttpStatusCode.Forbidden,
            "apps.forbidden");

    [Fact]
    public async Task ServiceAppUserRoles_HasAccessNull() =>
        Assert.Equal(
            JsonValueKind.Null,
            (await ResultAsync(await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Get, $"/auth/admin/apps/sandbox-api/users/{_operator.Id}/roles"))).GetProperty("hasAccess").ValueKind);

    [Fact]
    public async Task AppJournal_OnlyOwnAppEntries()
    {
        await SendAsync(_appAdmin, HttpMethod.Put, $"{APP}/users/{_operator.Id}/roles", new { roles = new[] { "accountant" } });
        await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Put, $"/auth/admin/apps/sandbox-api/users/{_operator.Id}/roles", new { roles = new[] { "manager" } });

        JsonElement page = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Get, $"{APP}/audit"));

        JsonElement entry = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("manager-1c", entry.GetProperty("app").GetString());
        Assert.Equal(2, _factory.AuditLog.Entries.Count);
    }

    [Fact]
    public async Task AppJournal_OtherApp_Forbidden() =>
        await AssertErrorAsync(await SendAsync(_appAdmin, HttpMethod.Get, "/auth/admin/apps/sandbox-api/audit"), HttpStatusCode.Forbidden, "apps.forbidden");

    [Fact]
    public async Task ProjectJournal_FilterByApp()
    {
        await SendAsync(_appAdmin, HttpMethod.Put, $"{APP}/users/{_operator.Id}/roles", new { roles = new[] { "accountant" } });
        await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Put, $"/auth/admin/apps/sandbox-api/users/{_operator.Id}/roles", new { roles = new[] { "manager" } });

        JsonElement page = await ResultAsync(await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Get, "/auth/admin/audit?app=sandbox-api"));

        Assert.Equal("sandbox-api", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("app").GetString());
    }

    [Fact]
    public async Task AppAdmin_OwnRoles_Forbidden() =>
        await AssertErrorAsync(
            await SendAsync(_appAdmin, HttpMethod.Put, $"{APP}/users/{_appAdmin.Id}/roles", new { roles = Array.Empty<string>() }),
            HttpStatusCode.Forbidden,
            "users.self_role");

    private static string[] Strings(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!)];

    // Старший 1С заводится только тестам, которым он нужен: его роли попадают в списки ролей и пользователей приложения.
    private void GivenLead()
    {
        _keycloak.AddAppRole(FakeIdentityAdmin.MANAGER_1C, "lead", "orders.view", "users.roles.manage");
        _keycloak.AddAppRole(FakeIdentityAdmin.MANAGER_1C, "viewer", "orders.view");
        _keycloak.Add(_lead, []);
        _keycloak.SetAppRoles(_lead, FakeIdentityAdmin.MANAGER_1C, "lead");
    }

    [Theory]
    [InlineData("operator")]    // orders.edit — у старшего его нет
    [InlineData("accountant")]  // reports.view — нет
    [InlineData("admin")]
    public async Task AppAdmin_RoleAboveOwnRights_Forbidden_NothingChanged(string role)
    {
        GivenLead();
        HttpResponseMessage response = await SendAsync(_lead, HttpMethod.Put, $"{APP}/users/{TestUser.Customer.Id}/roles", new { roles = new[] { role } });

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "users.role_above_own_rights");
        Assert.Empty(_keycloak.RoleChanges);
    }

    [Fact]
    public async Task AppAdmin_RoleWithinOwnRights_Ok()
    {
        GivenLead();
        JsonElement roles = await ResultAsync(await SendAsync(_lead, HttpMethod.Put, $"{APP}/users/{TestUser.Customer.Id}/roles", new { roles = new[] { "viewer" } }));

        Assert.Equal(["viewer"], [.. roles.GetProperty("roles").EnumerateArray().Select(role => role.GetString()!)]);
    }

    [Theory]
    [InlineData("viewer")]
    [InlineData("admin")]
    public async Task ProjectRoleManager_RoleInOtherApp_Forbidden_NoFreeHandInProjectApps(string role)
    {
        // users.roles.manage в токене открывает ему приложения проекта, но своих прав там у него нет: выдать роль с правами,
        // которых у него нет (а в чужом приложении их нет совсем), он не может — иначе выдал бы admin приложения второму аккаунту.
        GivenLead();
        var roleManager = new TestUser("менеджер ролей", Guid.NewGuid(), "+79990000010", [AuthModes.CUSTOMER, "role-manager"], "role-manager");
        _keycloak.AddAppRole("sandbox-api", "role-manager", "profile.view", "users.view", "users.roles.manage");
        _keycloak.Add(roleManager, ["role-manager"]);

        HttpResponseMessage response = await SendAsync(roleManager, HttpMethod.Put, $"{APP}/users/{TestUser.Customer.Id}/roles", new { roles = new[] { role } });

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "users.role_above_own_rights");
        Assert.Empty(_keycloak.RoleChanges);
    }

    [Fact]
    public async Task AppAdmin_WithRolesManage_RoleWithNewRight_Ok()
    {
        // Админ приложения (у него roles.manage) определяет и выдаёт роль с правом, которого нет в его собственной роли.
        _keycloak.AddAppRole(FakeIdentityAdmin.MANAGER_1C, "exporter", "orders.export");

        JsonElement roles = await ResultAsync(await SendAsync(_appAdmin, HttpMethod.Put, $"{APP}/users/{TestUser.Customer.Id}/roles", new { roles = new[] { "exporter" } }));

        Assert.Equal(["exporter"], [.. roles.GetProperty("roles").EnumerateArray().Select(role => role.GetString()!)]);
    }

    [Fact]
    public async Task ProjectAdmin_AnyRoleInProjectApp_Ok()
    {
        // Админ проекта управляет приложениями проекта по праву из токена; своих прав в «Менеджере 1С» у него нет.
        HttpResponseMessage response = await SendAsync(TestUser.AdminInAdminMode, HttpMethod.Put, $"{APP}/users/{TestUser.Customer.Id}/roles", new { roles = new[] { "admin" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(TestUser caller, HttpMethod method, string path, object? body = null, TimeSpan? loggedInAgo = null)
    {
        using HttpClient client = _factory.CreateClientFor(caller, DateTimeOffset.UtcNow - (loggedInAgo ?? TimeSpan.Zero));
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
