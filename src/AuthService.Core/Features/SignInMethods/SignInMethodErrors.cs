using SharedKernel;

namespace AuthService.Core.Features.SignInMethods;

public static class SignInMethodErrors
{
    /// <summary>Такого способа нет среди тех, что можно выключить.</summary>
    public static Error UnknownMethod(string method) =>
        Error.NotFound("sign_in_methods.unknown_method", $"Способ входа «{method}» нельзя включить или выключить.");

    /// <summary>Выключить нельзя — причина из SignInMethodRules (код ошибки — sign_in_methods.{причина}).</summary>
    public static Error CannotDisable(string blocker) =>
        Error.Conflict("sign_in_methods." + blocker, blocker switch
        {
            SignInMethodRules.NOT_AVAILABLE => "Этого способа входа у вас нет — выключать нечего.",
            SignInMethodRules.NEEDS_PASSKEY_OR_AUTHENTICATOR =>
                "Сначала добавьте ключ доступа или приложение-аутентификатор: без них этот способ выключить нельзя.",
            SignInMethodRules.LAST_METHOD => "Это последний способ входа: без него вы не сможете войти.",
            SignInMethodRules.RECOVERY_CODES_REQUIRED =>
                "Сначала создайте коды восстановления: они понадобятся, если телефон с приложением-аутентификатором потеряется.",
            _ => "Этот способ входа сейчас выключить нельзя.",
        });

    /// <summary>Такой внешний вход к аккаунту не привязан.</summary>
    public static Error NotLinked(string provider) =>
        Error.NotFound("sign_in_methods.not_linked", $"Внешний вход «{provider}» к вашему аккаунту не привязан.");

    /// <summary>Отвязать нельзя — причина из SignInMethodRules.UnlinkBlocker (код ошибки — sign_in_methods.{причина}).</summary>
    public static Error CannotUnlink(string blocker) =>
        Error.Conflict("sign_in_methods." + blocker, blocker switch
        {
            SignInMethodRules.LAST_METHOD =>
                "Это последний способ входа: сначала включите другой способ или добавьте ключ доступа.",
            _ => "Этот внешний вход сейчас отвязать нельзя.",
        });

    /// <summary>Вход по паролю выключен у человека — ссылка на новый пароль включила бы его обратно.</summary>
    public static Error PasswordDisabled() =>
        Error.Conflict("users.password_disabled", "Вход по паролю у пользователя выключен — сначала верните способы входа по умолчанию.");
}
