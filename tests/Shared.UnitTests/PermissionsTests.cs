using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth;
using Xunit;

namespace Shared.UnitTests;

public sealed class PermissionsTests
{
    private const string READ = "things.read";
    private const string DELETE = "things.delete";

    private readonly ServiceProvider _services = new ServiceCollection()
        .AddLogging()
        .AddPlatformAuthorization()
        .AddPlatformPermissions()
        .BuildServiceProvider();

    private IAuthorizationService Authorization => _services.GetRequiredService<IAuthorizationService>();

    [Fact]
    public void Resolver_ReadsPermissionsClaim() =>
        Assert.True(_services.GetRequiredService<IPermissionResolver>()
            .GetPermissions(User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN, [READ, DELETE])).SetEquals([READ, DELETE]));

    [Fact]
    public void Resolver_NoClaim_NoPermissions() =>
        Assert.Empty(_services.GetRequiredService<IPermissionResolver>().GetPermissions(User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN, [])));

    [Fact]
    public void Resolver_ServiceToken_TakesPermissionsOfOwnApp()
    {
        // Ключ «Заказов»: в сервисе входа — объявлять каталог, в «Уведомлениях» — слать уведомления.
        ClaimsPrincipal orders = Service("""{"notifications":["notifications.send"],"orders-reports":["reports.read"]}""", ["service.catalog.declare"]);

        Assert.True(Resolver("notifications").GetPermissions(orders).SetEquals(["notifications.send"]));
        Assert.True(Resolver("orders-reports").GetPermissions(orders).SetEquals(["reports.read"]));
        // Приложение, чьи права в permissions (сервис входа), — права из permissions.
        Assert.True(Resolver("auth-api").GetPermissions(orders).SetEquals(["service.catalog.declare"]));
    }

    [Fact]
    public void Resolver_UserToken_IgnoresAppPermissions()
    {
        // app_permissions бывает только у сервисов: у человека (без client_id) права — всегда из permissions.
        ClaimsPrincipal user = new(new ClaimsIdentity(
            [
                new(AuthClaims.PERMISSIONS, READ),
                new(AuthClaims.APP_PERMISSIONS, """{"notifications":["notifications.send"]}""", "JSON"),
            ],
            "test"));

        Assert.True(Resolver("notifications").GetPermissions(user).SetEquals([READ]));
    }

    [Fact]
    public void Resolver_BrokenAppPermissions_FallsBackToPermissions() =>
        Assert.True(Resolver("notifications").GetPermissions(Service("not json", [READ])).SetEquals([READ]));

    [Fact]
    public async Task PermissionPolicy_AllowsOnlyWhenModeGrantsIt()
    {
        // Права режима кладёт сервер входа: в режиме admin — оба, в режиме customer — только чтение.
        ClaimsPrincipal adminInAdminMode = User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN, [READ, DELETE]);
        ClaimsPrincipal adminInCustomerMode = User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.CUSTOMER, [READ]);

        Assert.True(await IsAllowedAsync(adminInAdminMode, DELETE));
        Assert.False(await IsAllowedAsync(adminInCustomerMode, DELETE));
        Assert.True(await IsAllowedAsync(adminInCustomerMode, READ));
    }

    [Fact]
    public async Task PermissionPolicy_UnknownPermission_Denied() =>
        Assert.False(await IsAllowedAsync(User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN, [READ, DELETE]), "things.unknown"));

    [Fact]
    public async Task PermissionPolicy_Anonymous_Denied() =>
        Assert.False(await IsAllowedAsync(new ClaimsPrincipal(new ClaimsIdentity()), READ));

    [Fact]
    public async Task RolePolicies_StillWorkAlongsidePermissions() =>
        Assert.True((await Authorization.AuthorizeAsync(User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN, []), AuthPolicies.ADMIN_ONLY)).Succeeded);

    [Fact]
    public void ToPolicyName_AddsPrefix() =>
        Assert.Equal("Permission:things.read", PermissionExtensions.ToPolicyName(READ));

    private async Task<bool> IsAllowedAsync(ClaimsPrincipal user, string permission) =>
        (await Authorization.AuthorizeAsync(user, PermissionExtensions.ToPolicyName(permission))).Succeeded;

    private static IPermissionResolver Resolver(string audience) =>
        new ServiceCollection()
            .AddPlatformPermissions()
            .AddSingleton(Options.Create(new JwtAuthenticationOptions { Audience = audience }))
            .BuildServiceProvider()
            .GetRequiredService<IPermissionResolver>();

    private static ClaimsPrincipal Service(string appPermissions, string[] permissions) =>
        new(new ClaimsIdentity(
            [
                new(AuthClaims.CLIENT_ID, "service-orders"),
                new(AuthClaims.APP_PERMISSIONS, appPermissions, "JSON"),
                .. permissions.Select(permission => new Claim(AuthClaims.PERMISSIONS, permission)),
            ],
            "test"));

    private static ClaimsPrincipal User(string[] roles, string activeMode, string[] permissions) =>
        new(new ClaimsIdentity(
            [
                new(AuthClaims.ACTIVE_MODE, activeMode),
                .. roles.Select(role => new Claim(AuthClaims.ROLE, role)),
                .. permissions.Select(permission => new Claim(AuthClaims.PERMISSIONS, permission)),
            ],
            "test",
            AuthClaims.SUBJECT,
            AuthClaims.ROLE));
}
