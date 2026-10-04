namespace AuthService.Core.Abstractions;

/// <summary>
/// Действие с учётной записью на странице сервера входа (тема проекта) по запросу из кабинета: пароль, почта, 2FA и
/// ключи доступа вводятся и подтверждаются там и не проходят через код сервиса входа.
/// </summary>
public enum AccountAction
{
    /// <summary>Сменить пароль (сервер входа сам попросит войти заново, если вход был давно).</summary>
    UpdatePassword,

    /// <summary>Сменить почту: новая подтверждается письмом со ссылкой.</summary>
    UpdateEmail,

    /// <summary>Подключить приложение-аутентификатор (2FA, одноразовые коды).</summary>
    ConfigureTotp,

    /// <summary>Добавить ключ доступа (passkey): вход без пароля — отпечатком, лицом или PIN-кодом устройства.</summary>
    RegisterPasskey,

    /// <summary>Создать коды восстановления (на случай, если приложения-аутентификатора нет под рукой); старые — недействительны.</summary>
    ConfigureRecoveryCodes,

    /// <summary>Удалить свои учётные данные (приложение, ключ доступа, коды) — после подтверждения на странице сервера входа.</summary>
    DeleteCredential,
}

/// <summary>Действие и, для удаления, какие учётные данные (Id на сервере входа).</summary>
public sealed record AccountActionRequest(AccountAction Action, string? CredentialId = null);

/// <summary>Имена действий в адресах сервиса (/auth/login?action=…&amp;credential=…).</summary>
public static class AccountActions
{
    private static readonly Dictionary<string, AccountAction> _byName = new(StringComparer.Ordinal)
    {
        ["update-password"] = AccountAction.UpdatePassword,
        ["update-email"] = AccountAction.UpdateEmail,
        ["configure-totp"] = AccountAction.ConfigureTotp,
        ["register-passkey"] = AccountAction.RegisterPasskey,
        ["configure-recovery-codes"] = AccountAction.ConfigureRecoveryCodes,
        ["delete-credential"] = AccountAction.DeleteCredential,
    };

    /// <summary>Итог действия после возврата: success, cancelled или error (как его сообщает сервер входа).</summary>
    public static readonly IReadOnlySet<string> Statuses = new HashSet<string>(StringComparer.Ordinal) { "success", "cancelled", "error" };

    public static bool TryParse(string name, out AccountAction action) => _byName.TryGetValue(name, out action);
}
