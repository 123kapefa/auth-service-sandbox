using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Ключи сервисов проекта: выдать, перевыпустить, отозвать — только админ проекта; Keycloak подменён.</summary>
public sealed class ServiceKeysTests : IClassFixture<AuthServiceWebFactory>
{
    // Право выпускать ключи (services.manage) без остальных админских прав: без users.view и с users.view.
    private static readonly TestUser _keyManager =
        new("менеджер ключей", Guid.NewGuid(), "+70000000011", [AuthModes.CUSTOMER, "key-manager"], "key-manager");

    private static readonly TestUser _keyReader =
        new("менеджер ключей с users.view", Guid.NewGuid(), "+70000000012", [AuthModes.CUSTOMER, "key-reader"], "key-reader");

    private readonly AuthServiceWebFactory _factory;

    public ServiceKeysTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        factory.ServiceRegistry.Reset();
        factory.IdentityAdmin.Reset();
        factory.IdentityAdmin.SeedAppRoles();
        factory.ServiceKeys.Reset();
        factory.Revocations.Reset();
        factory.AuditLog.Reset();
    }

    [Fact]
    public async Task Issue_RegistersClient_SecretShownOnce_Audited()
    {
        JsonElement issued = await ResultAsync(await IssueAsync("orders", "Заказы"));

        Assert.Equal("service-orders", issued.GetProperty("service").GetProperty("clientId").GetString());
        Assert.Equal(_factory.ServiceRegistry.SecretOf("service-orders"), issued.GetProperty("clientSecret").GetString());
        Assert.Equal(AuditActions.SERVICE_KEY_ISSUED, Assert.Single(_factory.AuditLog.Entries).Action);

        JsonElement list = await ResultAsync(await SendAsync(HttpMethod.Get, "/auth/admin/services"));
        JsonElement service = Assert.Single(list.EnumerateArray());
        Assert.Equal("Заказы", service.GetProperty("name").GetString());
        Assert.False(service.TryGetProperty("clientSecret", out _)); // в списке секрета нет
    }

    [Fact]
    public async Task Issue_WithPermissions_GrantedToServiceAccount()
    {
        JsonElement issued = await ResultAsync(await IssueAsync("orders", "Заказы", permissions: ["service.users.read"]));

        Assert.Equal(["service.users.read"], issued.GetProperty("service").GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        (Guid account, string[] grant, string[] _) = Assert.Single(_factory.IdentityAdmin.RoleChanges);
        Assert.Equal((_factory.ServiceRegistry.ServiceAccountOf("service-orders"), "service.users.read"), (account, Assert.Single(grant)));
    }

    [Fact]
    public async Task Issue_UserPermission_BadRequest()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await IssueAsync("orders", "Заказы", permissions: ["users.block"])).StatusCode);
        Assert.Empty(_factory.ServiceRegistry.ClientIds);
    }

    [Fact]
    public async Task SetPermissions_Narrowed_TokensRevoked()
    {
        await IssueAsync("orders", "Заказы", permissions: ["service.users.read", "service.access.check"]);

        using HttpClient client = _factory.CreateClientFor(TestUser.AdminInAdminMode);
        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri("/auth/admin/services/orders/permissions", UriKind.Relative), new { permissions = new[] { "service.access.check" } });

        Assert.Equal(["service.access.check"], (await ResultAsync(response)).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.True(_factory.Revocations.HasApp("service-orders", _factory.ServiceRegistry.ServiceAccountOf("service-orders")));
        Assert.Equal(AuditActions.SERVICE_KEY_PERMISSIONS_CHANGED, _factory.AuditLog.Entries[^1].Action);
    }

    [Fact]
    public async Task Issue_PermissionsNotGranted_ClientRemoved()
    {
        _factory.ServiceRegistry.SkipAccount = true; // учётки сервиса нет в Admin API — права не выдать

        HttpResponseMessage response = await IssueAsync("orders", "Заказы", permissions: ["service.users.read"]);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_factory.ServiceRegistry.ClientIds); // клиент не остался «ничьим»
        Assert.Empty(_factory.ServiceKeys.Keys);
    }

    [Fact]
    public async Task Issue_DatabaseFailed_ClientRemoved()
    {
        _factory.ServiceKeys.FailNextWrite = true;

        await AssertErrorAsync(null, await IssueAsync("orders", "Заказы"), HttpStatusCode.InternalServerError, "services.not_saved");

        Assert.Empty(_factory.ServiceRegistry.ClientIds);
    }

    [Fact]
    public async Task Rotate_DatabaseFailed_SecretStillReturned()
    {
        await IssueAsync("orders", "Заказы");
        _factory.ServiceKeys.FailNextWrite = true;

        JsonElement rotated = await ResultAsync(await SendAsync(HttpMethod.Post, "/auth/admin/services/orders/rotate"));

        // Keycloak уже сменил секрет — без ответа сервис остался бы без рабочего ключа.
        Assert.Equal(_factory.ServiceRegistry.SecretOf("service-orders"), rotated.GetProperty("clientSecret").GetString());
        Assert.True(_factory.Revocations.HasApp("service-orders", _factory.ServiceRegistry.ServiceAccountOf("service-orders")));
    }

    [Fact]
    public async Task Revoke_ClientGoneOutside_RecordRemoved_TokensRevoked()
    {
        await IssueAsync("orders", "Заказы");
        Guid serviceAccount = _factory.ServiceRegistry.ServiceAccountOf("service-orders");
        _factory.ServiceRegistry.Forget("service-orders"); // удалён в консоли Keycloak — токен управления не принят

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Delete, "/auth/admin/services/orders")).StatusCode);

        Assert.Empty(_factory.ServiceKeys.Keys);
        Assert.True(_factory.Revocations.HasApp("service-orders", serviceAccount));
        Assert.Contains("\"clientDeletionUnconfirmed\":true", _factory.AuditLog.Entries[^1].Changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_RegistrationTokenStoredEncrypted()
    {
        await IssueAsync("orders", "Заказы");

        string stored = Assert.Single(_factory.ServiceKeys.Keys).ProtectedRegistrationToken;
        Assert.DoesNotContain("rat-", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_SameCode_Conflict() =>
        await AssertErrorAsync(
            await IssueAsync("orders", "Заказы"),
            await IssueAsync("orders", "Заказы 2"),
            HttpStatusCode.Conflict,
            "services.exists");

    [Theory]
    [InlineData("Orders")]
    [InlineData("1orders")]
    [InlineData("orders-")]
    [InlineData("заказы")]
    public async Task Issue_InvalidCode_BadRequest(string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await IssueAsync(code, "Заказы")).StatusCode);
        Assert.Empty(_factory.ServiceRegistry.ClientIds);
    }

    [Fact]
    public async Task Issue_NoRegistrationPermit_Conflict()
    {
        _factory.ServiceRegistry.Configured = false;

        await AssertErrorAsync(null, await IssueAsync("orders", "Заказы"), HttpStatusCode.Conflict, "services.registration_unavailable");
    }

    [Fact]
    public async Task Rotate_NewSecret_OldTokensRevoked_Audited()
    {
        string first = (await ResultAsync(await IssueAsync("orders", "Заказы"))).GetProperty("clientSecret").GetString()!;

        JsonElement rotated = await ResultAsync(await SendAsync(HttpMethod.Post, "/auth/admin/services/orders/rotate"));

        string second = rotated.GetProperty("clientSecret").GetString()!;
        Assert.NotEqual(first, second);
        Assert.Equal(_factory.ServiceRegistry.SecretOf("service-orders"), second);
        Assert.True(_factory.Revocations.HasApp("service-orders", _factory.ServiceRegistry.ServiceAccountOf("service-orders")));
        Assert.Equal(AuditActions.SERVICE_KEY_ROTATED, _factory.AuditLog.Entries[^1].Action);

        // Токен управления сменился при перевыпуске — следующий перевыпуск идёт уже новым.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, "/auth/admin/services/orders/rotate")).StatusCode);
    }

    [Fact]
    public async Task Revoke_ClientDeleted_TokensRevoked_Audited()
    {
        await IssueAsync("orders", "Заказы");
        Guid serviceAccount = _factory.ServiceRegistry.ServiceAccountOf("service-orders");

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Delete, "/auth/admin/services/orders")).StatusCode);

        Assert.Empty(_factory.ServiceRegistry.ClientIds);
        Assert.Empty(_factory.ServiceKeys.Keys);
        Assert.True(_factory.Revocations.HasApp("service-orders", serviceAccount));
        Assert.Equal(AuditActions.SERVICE_KEY_REVOKED, _factory.AuditLog.Entries[^1].Action);
    }

    [Theory]
    [InlineData("POST", "/auth/admin/services/unknown/rotate")]
    [InlineData("DELETE", "/auth/admin/services/unknown")]
    public async Task UnknownService_NotFound(string method, string path) =>
        await AssertErrorAsync(null, await SendAsync(new HttpMethod(method), path), HttpStatusCode.NotFound, "services.not_found");

    public static TheoryData<string> NotProjectAdmins => new() { "manager", "admin-in-customer-mode", "customer" };

    [Theory]
    [MemberData(nameof(NotProjectAdmins))]
    public async Task NotProjectAdmin_Forbidden(string who)
    {
        TestUser caller = who switch
        {
            "manager" => TestUser.ManagerInManagerMode,
            "admin-in-customer-mode" => TestUser.AdminInCustomerMode,
            _ => TestUser.Customer,
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/auth/admin/services", caller)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await IssueAsync("orders", "Заказы", caller)).StatusCode);
        Assert.Empty(_factory.ServiceRegistry.ClientIds);
    }

    // Ключ не даёт больше, чем есть у выпускающего (OwnRights): service.users.read и service.access.check — это users.view,
    // service.catalog.declare — roles.manage, «приложение:право» — это право в приложении. Иначе держатель services.manage без
    // остальных прав выпустил бы ключ, читающий пользователей, и получил бы его секрет.
    [Theory]
    [InlineData("service.users.read")]
    [InlineData("service.access.check")]
    [InlineData("service.catalog.declare")]
    [InlineData("manager-1c:orders.view")]
    public async Task Issue_RightsAboveOwn_Forbidden_NothingRegistered(string permission)
    {
        GivenKeyManagers();

        HttpResponseMessage response = await IssueAsync("orders", "Заказы", _keyManager, [permission]);

        await AssertErrorAsync(null, response, HttpStatusCode.Forbidden, "services.key_above_own_rights");
        Assert.Empty(_factory.ServiceRegistry.ClientIds);
        Assert.Empty(_factory.IdentityAdmin.RoleChanges);
    }

    [Fact]
    public async Task Issue_RightsAboveOwn_MessageNamesMissingRights()
    {
        GivenKeyManagers();

        HttpResponseMessage response = await IssueAsync("orders", "Заказы", _keyManager, ["service.users.read", "manager-1c:orders.view"]);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string message = body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("message").GetString()!;
        Assert.Contains("service.users.read", message, StringComparison.Ordinal);
        Assert.Contains("manager-1c:orders.view", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_UsersRead_ByHolderOfUsersView_Ok()
    {
        GivenKeyManagers();

        HttpResponseMessage response = await IssueAsync("orders", "Заказы", _keyReader, ["service.users.read", "service.access.check"]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Issue_CatalogDeclare_ByHolderOfUsersView_StillForbidden()
    {
        GivenKeyManagers();

        HttpResponseMessage response = await IssueAsync("orders", "Заказы", _keyReader, ["service.catalog.declare"]);

        await AssertErrorAsync(null, response, HttpStatusCode.Forbidden, "services.key_above_own_rights");
    }

    [Fact]
    public async Task Issue_AnyRights_ByProjectAdmin_Ok()
    {
        // У админа проекта roles.manage: ограничения нет, ключ с правами в приложениях проекта и каталогом — как раньше.
        HttpResponseMessage response = await IssueAsync("orders", "Заказы", permissions: ["service.users.read", "service.catalog.declare", "manager-1c:orders.view"]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Rotate_KeyWithRightsAboveOwn_Forbidden_SecretNotChanged()
    {
        // Перевыпуск отдаёт новый секрет, то есть права ключа: без проверки перевыпуск чужого сильного ключа был бы обходом.
        GivenKeyManagers();
        await IssueAsync("orders", "Заказы", permissions: ["service.users.read"]);
        string? secretBefore = _factory.ServiceRegistry.SecretOf("service-orders");

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/auth/admin/services/orders/rotate", _keyManager);

        await AssertErrorAsync(null, response, HttpStatusCode.Forbidden, "services.key_above_own_rights");
        Assert.Equal(secretBefore, _factory.ServiceRegistry.SecretOf("service-orders"));
    }

    [Fact]
    public async Task Rotate_KeyWithinOwnRights_Ok()
    {
        GivenKeyManagers();
        await IssueAsync("orders", "Заказы", permissions: ["service.users.read"]);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/auth/admin/services/orders/rotate", _keyReader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Rotate_KeyWithoutRights_ByAnyHolder_Ok()
    {
        GivenKeyManagers();
        await IssueAsync("orders", "Заказы");

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/auth/admin/services/orders/rotate", _keyManager);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("grant")]     // выдать ключу право выше своих
    [InlineData("revoke")]    // забрать у ключа право выше своих
    public async Task SetPermissions_AboveOwn_Forbidden_NothingChanged(string direction)
    {
        GivenKeyManagers();
        await IssueAsync("orders", "Заказы", permissions: direction == "revoke" ? ["service.users.read"] : []);
        _factory.IdentityAdmin.RoleChanges.Clear();
        string[] wanted = direction == "grant" ? ["service.users.read"] : [];

        using HttpClient client = _factory.CreateClientFor(_keyManager);
        HttpResponseMessage response = await client.PutAsJsonAsync(new Uri("/auth/admin/services/orders/permissions", UriKind.Relative), new { permissions = wanted });

        await AssertErrorAsync(null, response, HttpStatusCode.Forbidden, "services.key_above_own_rights");
        Assert.Empty(_factory.IdentityAdmin.RoleChanges);
    }

    [Fact]
    public async Task SetPermissions_WithinOwn_Ok()
    {
        GivenKeyManagers();
        await IssueAsync("orders", "Заказы");

        using HttpClient client = _factory.CreateClientFor(_keyReader);
        HttpResponseMessage response = await client.PutAsJsonAsync(new Uri("/auth/admin/services/orders/permissions", UriKind.Relative), new { permissions = new[] { "service.users.read" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Issue_RightsAboveOwn_MessageNamesOnlyMissingRights()
    {
        // users.view есть (service.users.read — в пределах), права в приложении 1С нет — в сообщении только оно.
        GivenKeyManagers();

        HttpResponseMessage response = await IssueAsync("orders", "Заказы", _keyReader, ["service.users.read", "manager-1c:orders.view"]);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string message = body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("message").GetString()!;
        Assert.Contains("manager-1c:orders.view", message, StringComparison.Ordinal);
        Assert.DoesNotContain("service.users.read", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_AppRight_ByHolderOfRolesManageInThatApp_Ok()
    {
        // Админ приложения (roles.manage в его роли) определяет любые роли приложения: ключу с правом в нём ограничения нет.
        GivenKeyManagers();
        _factory.IdentityAdmin.SetAppRoles(_keyReader, FakeIdentityAdmin.MANAGER_1C, "admin");

        HttpResponseMessage response = await IssueAsync("orders", "Заказы", _keyReader, ["manager-1c:orders.view"]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Issue_AppRight_ByHolderOfThatRightInApp_Ok()
    {
        GivenKeyManagers();
        _factory.IdentityAdmin.AddAppRole(FakeIdentityAdmin.MANAGER_1C, "viewer", "orders.view");
        _factory.IdentityAdmin.SetAppRoles(_keyReader, FakeIdentityAdmin.MANAGER_1C, "viewer");

        HttpResponseMessage ok = await IssueAsync("orders", "Заказы", _keyReader, ["manager-1c:orders.view"]);
        HttpResponseMessage above = await IssueAsync("orders2", "Заказы 2", _keyReader, ["manager-1c:orders.edit"]);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await AssertErrorAsync(null, above, HttpStatusCode.Forbidden, "services.key_above_own_rights");
    }

    [Fact]
    public async Task Revoke_StrongKey_ByWeakHolder_Ok_EmergencyStop()
    {
        // Отзыв — аварийное действие: утёкший сильный ключ гасится любым держателем services.manage; прав он не даёт.
        GivenKeyManagers();
        await IssueAsync("orders", "Заказы", permissions: ["service.users.read", "service.catalog.declare"]);

        HttpResponseMessage response = await SendAsync(HttpMethod.Delete, "/auth/admin/services/orders", _keyManager);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("service-orders", _factory.ServiceRegistry.ClientIds);
    }

    // Менеджеры ключей заводятся только тестам, которым они нужны.
    private void GivenKeyManagers()
    {
        _factory.IdentityAdmin.Add(_keyManager, []);
        _factory.IdentityAdmin.Add(_keyReader, []);
    }

    [Fact]
    public async Task Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/auth/admin/services", UriKind.Relative))).StatusCode);
    }

    private async Task<HttpResponseMessage> IssueAsync(string code, string name, TestUser? caller = null, string[]? permissions = null)
    {
        using HttpClient client = _factory.CreateClientFor(caller ?? TestUser.AdminInAdminMode);
        return await client.PostAsJsonAsync(new Uri("/auth/admin/services", UriKind.Relative), new { code, name, permissions });
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, TestUser? caller = null)
    {
        using HttpClient client = _factory.CreateClientFor(caller ?? TestUser.AdminInAdminMode);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("result").Clone();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage? first, HttpResponseMessage response, HttpStatusCode status, string code)
    {
        if (first is not null)
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        Assert.Equal(status, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString());
    }
}
