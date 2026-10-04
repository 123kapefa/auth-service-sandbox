using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth;
using Xunit;

namespace Shared.UnitTests;

public sealed class AuthPoliciesTests
{
    private readonly IAuthorizationService _authorization = new ServiceCollection()
        .AddLogging()
        .AddPlatformAuthorization()
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    // Кто → какие политики проходит. Остальные политики для этого пользователя должны отказывать.
    public static TheoryData<string, string[], string?, string[]> Matrix => new()
    {
        { "покупатель", [AuthModes.CUSTOMER], AuthModes.CUSTOMER, [] },
        { "админ в режиме покупателя", [AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.CUSTOMER, [] },
        {
            "админ в режиме admin", [AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.ADMIN,
            [AuthPolicies.ADMIN_ONLY, AuthPolicies.MANAGER_OR_ADMIN]
        },
        { "менеджер в режиме manager", [AuthModes.CUSTOMER, AuthModes.MANAGER], AuthModes.MANAGER, [AuthPolicies.MANAGER_OR_ADMIN] },
        { "менеджер в режиме покупателя", [AuthModes.CUSTOMER, AuthModes.MANAGER], AuthModes.CUSTOMER, [] },
        { "режим admin без роли admin", [AuthModes.CUSTOMER], AuthModes.ADMIN, [] },
        { "админ в режиме manager без роли manager", [AuthModes.CUSTOMER, AuthModes.ADMIN], AuthModes.MANAGER, [] },
        { "без active_mode", [AuthModes.CUSTOMER, AuthModes.ADMIN], null, [] },
    };

    private static readonly string[] _allPolicies =
    [
        AuthPolicies.ADMIN_ONLY,
        AuthPolicies.MANAGER_OR_ADMIN,
    ];

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Policies_AllowOnlyMatchingRoleAndMode(string who, string[] roles, string? activeMode, string[] allowed)
    {
        ClaimsPrincipal user = CreateUser(roles, activeMode);

        foreach (string policy in _allPolicies)
        {
            AuthorizationResult result = await _authorization.AuthorizeAsync(user, policy);
            Assert.True(result.Succeeded == allowed.Contains(policy), $"{who}: {policy} → {result.Succeeded}");
        }
    }

    [Fact]
    public async Task Anonymous_PassesNoPolicy()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        foreach (string policy in _allPolicies)
        {
            Assert.False((await _authorization.AuthorizeAsync(anonymous, policy)).Succeeded, policy);
        }
    }

    private static ClaimsPrincipal CreateUser(string[] roles, string? activeMode)
    {
        List<Claim> claims = [new(AuthClaims.SUBJECT, "user-1"), .. roles.Select(role => new Claim(AuthClaims.ROLE, role))];
        if (activeMode is not null)
        {
            claims.Add(new Claim(AuthClaims.ACTIVE_MODE, activeMode));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", AuthClaims.SUBJECT, AuthClaims.ROLE));
    }
}
