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

    /// <summary>Свою роль менять нельзя: так админ не отнимет права у себя по ошибке.</summary>
    public static Error SelfRole() =>
        Error.Authorization("users.self_role", "Нельзя менять свою роль.");

    /// <summary>Выдача admin — только после недавнего входа: украденная сессия не раздаст права.</summary>
    public static Error ReauthenticationRequired() =>
        Error.Authorization(
            "auth.reauthentication_required",
            "Подтвердите, что это вы: войдите заново (GET /auth/login?reauth=true) и повторите действие.");

    /// <summary>Свой доступ к приложению закрывать нельзя.</summary>
    public static Error SelfAccess() =>
        Error.Authorization("users.self_access", "Нельзя менять свой доступ к приложению.");

    /// <summary>Нет права на это приложение: ни в токене (админ проекта), ни среди ролей в самом приложении.</summary>
    public static Error AppForbidden() =>
        Error.Authorization("apps.forbidden", "Нет права управлять этим приложением.");

    /// <summary>Нет такого приложения (или служебной учётке не дано право на его клиента).</summary>
    public static Error AppNotFound() =>
        Error.NotFound("apps.not_found", "Приложение не найдено.");

    /// <summary>Системное право объявлено кодом сервиса — его проверяет код; удалить нельзя.</summary>
    public static Error SystemPermission(string name) =>
        Error.Conflict("permissions.system", $"Право «{name}» объявлено кодом сервиса — удалить нельзя.");

    /// <summary>Это роль, а не право.</summary>
    public static Error NotPermission(string name) =>
        Error.Validation("permissions.not_permission", $"«{name}» — роль, а не право.", "name");

    /// <summary>Встроенную роль удалить нельзя.</summary>
    public static Error BuiltinRole(string name) =>
        Error.Conflict("roles.builtin", $"Роль «{name}» встроенная — удалить нельзя.");

    /// <summary>У admin нельзя забрать управление ролями — иначе управлять ими станет некому.</summary>
    public static Error RolesLockout() =>
        Error.Conflict("roles.lockout", "У роли admin нельзя убрать roles.manage — иначе управлять ролями станет некому.");

    /// <summary>Таких прав нет в каталоге приложения.</summary>
    public static Error UnknownPermissions(IEnumerable<string> names) =>
        Error.Validation("roles.unknown_permission", $"Нет таких прав: {string.Join(", ", names)}.", "permissions");

    /// <summary>Таких ролей нет в приложении.</summary>
    public static Error UnknownRoles(IEnumerable<string> names) =>
        Error.Validation("roles.unknown_role", $"Нет таких ролей: {string.Join(", ", names)}.", "roles");

    /// <summary>Это право, а не роль.</summary>
    public static Error NotRole(string name) =>
        Error.Validation("roles.not_role", $"«{name}» — право, а не роль.", "name");
}
