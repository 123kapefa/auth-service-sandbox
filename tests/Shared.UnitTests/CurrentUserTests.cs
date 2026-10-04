using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth;
using Xunit;

namespace Shared.UnitTests;

public sealed class CurrentUserTests
{
    private const string USER_ID = "fe85c200-77cc-4f4e-8d33-85d2d5e22079";

    [Fact]
    public void AuthenticatedUser_ReadsTokenAndPermissionsOfActiveMode()
    {
        CurrentUser user = Resolve(new ClaimsIdentity(
            [
                new(AuthClaims.SUBJECT, USER_ID),
                new(AuthClaims.PHONE, "+71234567890"),
                new(AuthClaims.ACTIVE_MODE, AuthModes.ADMIN),
                new(AuthClaims.ROLE, AuthModes.CUSTOMER),
                new(AuthClaims.ROLE, AuthModes.ADMIN),
                new(AuthClaims.AVAILABLE_ROLES, AuthModes.CUSTOMER),
                new(AuthClaims.AVAILABLE_ROLES, AuthModes.ADMIN),
                new(AuthClaims.PERMISSIONS, "things.delete"),
            ],
            "test",
            AuthClaims.SUBJECT,
            AuthClaims.ROLE));

        Assert.True(user.IsAuthenticated);
        Assert.Equal(Guid.Parse(USER_ID), user.Id);
        Assert.Equal("+71234567890", user.Phone);
        Assert.Equal(AuthModes.ADMIN, user.ActiveMode);
        Assert.Equal([AuthModes.CUSTOMER, AuthModes.ADMIN], user.Roles);
        Assert.Equal([AuthModes.CUSTOMER, AuthModes.ADMIN], user.AvailableModes);
        Assert.True(user.HasPermission("things.delete"));
    }

    [Fact]
    public void Anonymous_HasNothing()
    {
        CurrentUser user = Resolve(new ClaimsIdentity());

        Assert.False(user.IsAuthenticated);
        Assert.Null(user.Id);
        Assert.Empty(user.Roles);
        Assert.Empty(user.Permissions);
    }

    [Fact]
    public void SubjectNotUuid_IdIsNull() =>
        Assert.Null(Resolve(new ClaimsIdentity([new(AuthClaims.SUBJECT, "service-account")], "test")).Id);

    private static CurrentUser Resolve(ClaimsIdentity identity)
    {
        ServiceProvider services = new ServiceCollection()
            .AddPlatformPermissions()
            .BuildServiceProvider();

        services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return services.CreateScope().ServiceProvider.GetRequiredService<CurrentUser>();
    }
}
