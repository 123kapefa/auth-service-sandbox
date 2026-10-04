using PlatformAuth;

namespace AuthService.Core.Features.Admin;

/// <summary>Кого видит вызвавший: админ (режим admin) — всех, менеджер — только покупателей.</summary>
public static class AdminScope
{
    public static bool OnlyCustomers(CurrentUser caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return !string.Equals(caller.ActiveMode, AuthModes.ADMIN, StringComparison.Ordinal);
    }
}
