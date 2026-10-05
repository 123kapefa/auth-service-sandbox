using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts.Services;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.IntegrationTests.Infrastructure;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>
/// Каталог приложения из его кода (PUT /auth/services/catalog): права — главные в коде, роли заводятся один раз и
/// дальше принадлежат админу; объявить можно только своё приложение (код ключа) и только с правом на это.
/// </summary>
public sealed class AppCatalogTests : IClassFixture<AuthServiceWebFactory>
{
    private const string PATH = "/auth/services/catalog";
    private const string ORDERS = "orders";
    private static readonly Guid _key = Guid.NewGuid();

    private static readonly DeclareCatalogRequest _catalog = new(
        [new("orders.view", "Свои заказы"), new("orders.create", "Оформить заказ"), new("orders.manage", "Все заказы")],
        [new("customer", "Покупатель", ["orders.view", "orders.create"]), new("manager", "Сотрудник", ["orders.view", "orders.manage"])]);

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public AppCatalogTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        _keycloak.SeedAppRoles();
        _keycloak.AddApp(ORDERS);
    }

    [Fact]
    public async Task Declare_CreatesCodePermissionsAndBuiltinRoles()
    {
        JsonElement result = await ResultAsync(await DeclareAsync(_catalog));

        Assert.Equal(ORDERS, result.GetProperty("app").GetString());
        Assert.Equal(["orders.view", "orders.create", "orders.manage"], Names(result, "createdPermissions"));
        Assert.Equal(["customer", "manager"], Names(result, "createdRoles"));

        AppRole view = Single("orders.view");
        Assert.Equal(AppRoleKinds.PERMISSION, view.Kind);
        Assert.Equal(AppRoleKinds.SOURCE_CODE, view.Source);
        AppRole manager = Single("manager");
        Assert.True(manager.Builtin);
        Assert.Equal(AppRoleKinds.SOURCE_CODE, manager.Source);
        Assert.NotNull(manager.Version);
        Assert.Equal(["orders.manage", "orders.view"], _keycloak.CompositesOf(ORDERS, "manager").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Declare_Again_KeepsAdminChangesToRoles()
    {
        await ResultAsync(await DeclareAsync(_catalog));
        // Админ убрал у сотрудника «все заказы» — следующий старт сервиса это не откатывает.
        await _keycloak.SetRoleCompositesAsync(ORDERS, "manager", [], ["orders.manage"], default);

        JsonElement again = await ResultAsync(await DeclareAsync(_catalog));

        Assert.Empty(Names(again, "createdPermissions"));
        Assert.Empty(Names(again, "createdRoles"));
        Assert.Equal(["customer", "manager"], Names(again, "keptRoles"));
        Assert.Equal(["orders.view"], _keycloak.CompositesOf(ORDERS, "manager"));
    }

    [Fact]
    public async Task Declare_AdoptsEmptyDefaultRoleFromAddApp()
    {
        _keycloak.AddApp(ORDERS, defaultRole: "customer");

        JsonElement result = await ResultAsync(await DeclareAsync(_catalog));

        Assert.Contains("customer", Names(result, "createdRoles"));
        Assert.Equal(AppRoleKinds.SOURCE_CODE, Single("customer").Source);
        Assert.Equal("Покупатель", Single("customer").Description);
        Assert.Equal(["orders.create", "orders.view"], _keycloak.CompositesOf(ORDERS, "customer").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Declare_AdminPermissionBecomesCode_OrphansReported()
    {
        await ResultAsync(await DeclareAsync(_catalog));
        await _keycloak.CreateAppRoleAsync(ORDERS,
            new AppRole(null, "orders.export", null, AppRoleKinds.PERMISSION, AppRoleKinds.SOURCE_ADMIN, false), default);

        JsonElement result = await ResultAsync(await DeclareAsync(new DeclareCatalogRequest(
            [new("orders.view", "Свои заказы"), new("orders.export", "Выгрузка")], [])));

        Assert.Equal(["orders.export"], Names(result, "updatedPermissions"));
        Assert.Equal(AppRoleKinds.SOURCE_CODE, Single("orders.export").Source);
        // Исчезли из кода — не удалены (могут быть в ролях), только названы.
        Assert.Equal(["orders.create", "orders.manage"], Names(result, "orphans"));
        Assert.Equal(AppRoleKinds.PERMISSION, Single("orders.manage").Kind);
    }

    [Fact]
    public async Task Declare_RoleWithUnknownPermission_NothingWritten()
    {
        HttpResponseMessage response = await DeclareAsync(new DeclareCatalogRequest(
            [new("orders.view", null)], [new("customer", null, ["orders.view", "orders.missing"])]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(_keycloak.AppRolesOf(ORDERS), role => role.Name == "orders.view");
    }

    [Theory]
    [InlineData("orders.View")]
    [InlineData("orders")]
    [InlineData("")]
    public async Task Declare_BadPermissionName_Rejected(string name) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await DeclareAsync(new DeclareCatalogRequest([new(name, null)], []))).StatusCode);

    [Fact]
    public async Task Declare_AccessDeniedRole_Rejected() =>
        Assert.Equal(HttpStatusCode.BadRequest,
            (await DeclareAsync(new DeclareCatalogRequest([], [new(AppRoles.ACCESS_DENIED, null, [])]))).StatusCode);

    [Fact]
    public async Task Declare_UnknownApp_NotFound() =>
        await AssertErrorAsync(await DeclareAsync(_catalog, "service-warehouse"), HttpStatusCode.NotFound, "catalog.app_not_found");

    [Fact]
    public async Task Declare_AuthServiceApp_Forbidden() =>
        await AssertErrorAsync(await DeclareAsync(_catalog, $"service-{AuthServiceWebFactory.AUDIENCE}"), HttpStatusCode.Forbidden,
            "catalog.auth_service_app");

    [Fact]
    public async Task Declare_KeyWithoutPermission_Forbidden()
    {
        using HttpClient client = _factory.CreateClientWithToken(
            _factory.CreateServiceToken($"service-{ORDERS}", _key, AuthServicePermissions.ServiceAccess.USERS_READ));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(new Uri(PATH, UriKind.Relative), _catalog)).StatusCode);
        Assert.DoesNotContain(_keycloak.AppRolesOf(ORDERS), role => role.Name == "orders.view");
    }

    [Fact]
    public async Task Human_EvenAdmin_Forbidden()
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.AdminInAdminMode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(new Uri(PATH, UriKind.Relative), _catalog)).StatusCode);
    }

    private AppRole Single(string name) => _keycloak.AppRolesOf(ORDERS).Single(role => role.Name == name);

    private async Task<HttpResponseMessage> DeclareAsync(DeclareCatalogRequest catalog, string clientId = $"service-{ORDERS}")
    {
        using HttpClient client = _factory.CreateClientWithToken(
            _factory.CreateServiceToken(clientId, _key, AuthServicePermissions.ServiceAccess.CATALOG_DECLARE));
        return await client.PutAsJsonAsync(new Uri(PATH, UriKind.Relative), catalog);
    }

    private static string[] Names(JsonElement result, string property) =>
        [.. result.GetProperty(property).EnumerateArray().Select(name => name.GetString() ?? string.Empty)];

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
