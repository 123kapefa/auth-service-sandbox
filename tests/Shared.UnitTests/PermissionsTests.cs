using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
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
        .AddPlatformPermissions<TestRolePermissions>()
        .BuildServiceProvider();

    private IAuthorizationService Authorization => _services.GetRequiredService<IAuthorizationService>();

    [Fact]
    public void Resolver_UsesActiveModeNotAllRoles()
    {
        IPermissionResolver resolver = _services.GetRequiredService<IPermissionResolver>();

        Assert.True(resolver.GetPermissions(User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.CUSTOMER)).SetEquals([READ]));
        Assert.True(resolver.GetPermissions(User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN)).SetEquals([READ, DELETE]));
    }

    [Fact]
    public void Resolver_ModeWithoutRole_GivesNothing() =>
        Assert.Empty(_services.GetRequiredService<IPermissionResolver>().GetPermissions(User([AuthModes.CUSTOMER], AuthModes.ADMIN)));

    [Fact]
    public async Task PermissionPolicy_AllowsOnlyWhenModeGrantsIt()
    {
        ClaimsPrincipal adminInAdminMode = User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN);
        ClaimsPrincipal adminInCustomerMode = User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.CUSTOMER);

        Assert.True(await IsAllowedAsync(adminInAdminMode, DELETE));
        Assert.False(await IsAllowedAsync(adminInCustomerMode, DELETE));
        Assert.True(await IsAllowedAsync(adminInCustomerMode, READ));
    }

    [Fact]
    public async Task PermissionPolicy_UnknownPermission_Denied() =>
        Assert.False(await IsAllowedAsync(User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN), "things.unknown"));

    [Fact]
    public async Task PermissionPolicy_Anonymous_Denied() =>
        Assert.False(await IsAllowedAsync(new ClaimsPrincipal(new ClaimsIdentity()), READ));

    [Fact]
    public async Task RolePolicies_StillWorkAlongsidePermissions() =>
        Assert.True((await Authorization.AuthorizeAsync(User([AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN), AuthPolicies.ADMIN_ONLY)).Succeeded);

    [Fact]
    public void ToPolicyName_AddsPrefix() =>
        Assert.Equal("Permission:things.read", PermissionExtensions.ToPolicyName(READ));

    private async Task<bool> IsAllowedAsync(ClaimsPrincipal user, string permission) =>
        (await Authorization.AuthorizeAsync(user, PermissionExtensions.ToPolicyName(permission))).Succeeded;

    private static ClaimsPrincipal User(string[] roles, string activeMode) =>
        new(new ClaimsIdentity(
            [new(AuthClaims.ACTIVE_MODE, activeMode), .. roles.Select(role => new Claim(AuthClaims.ROLE, role))],
            "test",
            AuthClaims.SUBJECT,
            AuthClaims.ROLE));

    private sealed class TestRolePermissions : IRolePermissionMap
    {
        public IReadOnlyCollection<string> GetPermissions(string role) => role switch
        {
            AuthModes.CUSTOMER => [READ],
            AuthModes.ADMIN => [READ, DELETE],
            _ => [],
        };
    }
}
