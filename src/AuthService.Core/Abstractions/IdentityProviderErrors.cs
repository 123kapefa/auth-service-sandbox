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

    /// <summary>logout_token не прошёл проверку (подпись, издатель, адресат, событие).</summary>
    public static Error LogoutTokenInvalid() =>
        Error.Validation("auth.logout_token.invalid", "logout_token недействителен.");

    /// <summary>Нет такого приложения (клиента с ролями) или служебной учётке не дано право на него.</summary>
    public static Error AppNotFound() =>
        Error.NotFound("apps.not_found", "Приложение не найдено.");

    /// <summary>Роль или право с таким именем уже есть.</summary>
    public static Error RoleExists(string name) =>
        Error.Conflict("roles.exists", $"Роль или право «{name}» уже есть.");

    /// <summary>Нет такой роли или права.</summary>
    public static Error RoleNotFound(string name) =>
        Error.NotFound("roles.not_found", $"Роль или право «{name}» не найдено.");

    /// <summary>Пользователя с таким id нет.</summary>
    public static Error UserNotFound() =>
        Error.NotFound("users.not_found", "Пользователь не найден.");
}
