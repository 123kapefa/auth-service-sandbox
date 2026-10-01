using AuthService.Core.Authorization;
using PlatformAuth;
using Xunit;

namespace AuthService.UnitTests;

// Карта прав, согласованная с владельцем. Меняется карта — меняется и этот тест.
public sealed class AuthServiceRolePermissionsTests
{
    private readonly AuthServiceRolePermissions _map = new();

    public static TheoryData<string, string[]> AgreedMap => new()
    {
        { AuthModes.CUSTOMER, [AuthServicePermissions.Profiles.VIEW] },
        {
            AuthModes.MANAGER,
            [AuthServicePermissions.Profiles.VIEW, AuthServicePermissions.Users.VIEW, AuthServicePermissions.Users.BLOCK]
        },
        {
            AuthModes.ADMIN,
            [
                AuthServicePermissions.Profiles.VIEW,
                AuthServicePermissions.Users.VIEW,
                AuthServicePermissions.Users.BLOCK,
                AuthServicePermissions.Users.MANAGE_ROLES,
                AuthServicePermissions.Users.MANAGE_SESSIONS,
                AuthServicePermissions.Audit.VIEW,
            ]
        },
    };

    [Theory]
    [MemberData(nameof(AgreedMap))]
    public void Role_HasExactlyAgreedPermissions(string role, string[] expected) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), _map.GetPermissions(role).Order(StringComparer.Ordinal));

    [Fact]
    public void UnknownRole_HasNoPermissions() =>
        Assert.Empty(_map.GetPermissions("courier"));
}
