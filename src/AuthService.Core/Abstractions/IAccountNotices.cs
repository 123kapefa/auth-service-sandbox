using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Abstractions;

/// <summary>
/// Уведомления человеку об изменениях в его аккаунте — письмом в оформлении проекта, а без почты — SMS (их шлёт
/// сервер входа: шаблоны писем и телефония — там).
/// </summary>
public interface IAccountNotices
{
    /// <summary>Способы входа изменились. Успех — как ушло: email, sms или none (некуда).</summary>
    Task<Result<string, Error>> SignInMethodsChangedAsync(Guid userId, SignInMethodNotice notice, CancellationToken cancellationToken);
}

/// <summary>Что изменилось: способ выключен или включён (Method — какой), или админ вернул способы по умолчанию.</summary>
/// <param name="Event">disabled, enabled или reset.</param>
/// <param name="Method">password, phone-code, email-code; у reset — null.</param>
public sealed record SignInMethodNotice(string Event, string? Method)
{
    public const string DISABLED = "disabled";
    public const string ENABLED = "enabled";
    public const string RESET = "reset";
}
