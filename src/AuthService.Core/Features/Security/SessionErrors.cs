using SharedKernel;

namespace AuthService.Core.Features.Security;

public static class SessionErrors
{
    /// <summary>Нет такой сессии у этого пользователя (в том числе — чужая).</summary>
    public static Error NotFound() =>
        Error.NotFound("sessions.not_found", "Сессия не найдена.");
}
