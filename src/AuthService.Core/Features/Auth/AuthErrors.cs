using SharedKernel;

namespace AuthService.Core.Features.Auth;

public static class AuthErrors
{
    private const string STATE_MISSING = "auth.login.state_missing";
    private const string STATE_MISMATCH = "auth.login.state_mismatch";

    public static Error LoginStateMissing() =>
        Error.Validation(STATE_MISSING, "Вход устарел или не был начат. Начните вход заново.");

    public static Error LoginStateMismatch() =>
        Error.Validation(STATE_MISMATCH, "Ответ сервера входа не относится к этому входу. Начните вход заново.");

    /// <summary>Сервис не узнал вход, с которого вернулся браузер: метка входа истекла, ушла в другую вкладку и т.п.</summary>
    public static bool IsLostLogin(Error error) =>
        error.Messages.Any(message => message.Code is STATE_MISSING or STATE_MISMATCH);

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
