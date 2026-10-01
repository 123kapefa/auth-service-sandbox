using SharedKernel;

namespace AuthService.Core.Features.Admin;

public static class AdminErrors
{
    /// <summary>Менеджер работает только с покупателями — как в боевом сервисе.</summary>
    public static Error StaffNotAllowed() =>
        Error.Authorization("users.staff_forbidden", "Менеджер работает только с покупателями.");

    /// <summary>Свою учётную запись блокировать и разблокировать нельзя.</summary>
    public static Error SelfRestriction() =>
        Error.Authorization("users.self_restriction", "Нельзя заблокировать или разблокировать себя.");
}
