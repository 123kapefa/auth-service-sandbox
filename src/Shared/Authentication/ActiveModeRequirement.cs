using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace PlatformAuth;

/// <summary>
/// Пользователь работает в одном из разрешённых режимов И у него есть роль, совпадающая с режимом.
/// Админ в режиме покупателя админских прав не получает; режим без соответствующей роли не даёт ничего.
/// </summary>
public sealed class ActiveModeRequirement(params string[] allowedModes) : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> AllowedModes { get; } = allowedModes;
}

internal sealed class ActiveModeRequirementHandler : AuthorizationHandler<ActiveModeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveModeRequirement requirement)
    {
        string? activeMode = context.User.FindFirstValue(AuthClaims.ACTIVE_MODE);
        if (activeMode is not null
            && requirement.AllowedModes.Contains(activeMode, StringComparer.Ordinal)
            && context.User.IsInRole(activeMode))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
