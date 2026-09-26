using SharedKernel;

namespace AuthService.Core.Abstractions;

public static class IdentityProviderErrors
{
    /// <summary>Код или refresh-токен не принят: истёк, уже использован, сессия завершена или пользователь заблокирован.</summary>
    public static Error GrantRejected() =>
        Error.Authentication("auth.session.invalid", "Сессия недействительна. Войдите заново.");

    /// <summary>Сервер входа недоступен или ответил ошибкой — можно повторить позже.</summary>
    public static Error Unavailable() =>
        Error.Failure("auth.provider.unavailable", "Сервер входа временно недоступен.").AsTransient();
}
