namespace AuthService.Core.Abstractions;

/// <summary>
/// Действие с учётной записью на странице сервера входа (тема проекта) по запросу из кабинета: пароль и почта
/// вводятся там и не проходят через код сервиса входа.
/// </summary>
public enum AccountAction
{
    /// <summary>Сменить пароль (сервер входа сам попросит войти заново, если вход был давно).</summary>
    UpdatePassword,

    /// <summary>Сменить почту: новая подтверждается письмом со ссылкой.</summary>
    UpdateEmail,
}

/// <summary>Имена действий в адресах сервиса (/auth/login?action=…).</summary>
public static class AccountActions
{
    private static readonly Dictionary<string, AccountAction> _byName = new(StringComparer.Ordinal)
    {
        ["update-password"] = AccountAction.UpdatePassword,
        ["update-email"] = AccountAction.UpdateEmail,
    };

    /// <summary>Итог действия после возврата: success, cancelled или error (как его сообщает сервер входа).</summary>
    public static readonly IReadOnlySet<string> Statuses = new HashSet<string>(StringComparer.Ordinal) { "success", "cancelled", "error" };

    public static bool TryParse(string name, out AccountAction action) => _byName.TryGetValue(name, out action);
}
