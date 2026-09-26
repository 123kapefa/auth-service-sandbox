using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace PlatformAuth;

/// <summary>
/// Политики «Permission:xxx» строятся по имени на лету — объявлять каждое право заранее не нужно.
/// Остальные имена (AdminOnly и т.д.) — обычные политики из AddAuthorization.
/// </summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string POLICY_PREFIX = "Permission:";

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        if (!policyName.StartsWith(POLICY_PREFIX, StringComparison.Ordinal))
        {
            return base.GetPolicyAsync(policyName);
        }

        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[POLICY_PREFIX.Length..]))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
