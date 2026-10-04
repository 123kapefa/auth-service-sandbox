using SharedKernel;

namespace AuthService.Core.Features.Auth;

public static class AuthErrors
{
    public static Error LoginStateMissing() =>
        Error.Validation("auth.login.state_missing", "Вход устарел или не был начат. Начните вход заново.");

    public static Error LoginStateMismatch() =>
        Error.Validation("auth.login.state_mismatch", "Ответ сервера входа не относится к этому входу. Начните вход заново.");

    public static Error LoginDenied(string reason) =>
        Error.Authentication("auth.login.denied", $"Вход не выполнен: {reason}.");

    public static Error SessionMissing() =>
        Error.Authentication("auth.session.missing", "Нет активной сессии. Войдите.");

    /// <summary>Режим неизвестен или у пользователя нет такой роли.</summary>
    public static Error ModeNotAvailable() =>
        Error.Authorization("auth.mode.not_available", "Этот режим вам недоступен.");

    public static Error UnknownAccountAction(string action) =>
        Error.Validation("auth.action.unknown", $"Неизвестное действие с учётной записью: «{action}».");

    public static Error CredentialRequired() =>
        Error.Validation("auth.action.credential_required", "Укажите, что удалить: credential — Id учётных данных.");

    public static Error UserBlocked() =>
        Error.Authentication("auth.user.blocked", "Учётная запись заблокирована.");
}
