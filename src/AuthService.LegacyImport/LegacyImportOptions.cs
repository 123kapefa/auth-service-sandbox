namespace AuthService.LegacyImport;

/// <summary>Настройки переноса. Секрет клиента и строка подключения в бою — только из переменных окружения.</summary>
public sealed class LegacyImportOptions
{
    public const string SECTION = "LegacyImport";

    /// <summary>Сколько пользователей в одном запросе partialImport.</summary>
    public int BatchSize { get; init; } = 500;

    public KeycloakTarget Keycloak { get; init; } = new();
}

public sealed class KeycloakTarget
{
    public Uri BaseUrl { get; init; } = new("http://localhost:8180");
    public string Realm { get; init; } = "sandbox";

    /// <summary>Временный клиент переноса: manage-realm (partialImport сохраняет Id), manage-users, view-users.</summary>
    public string ClientId { get; init; } = "legacy-import";
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>Клиент, на котором роли сотрудников (manager, admin) — приложение сервиса.</summary>
    public string RolesClientId { get; init; } = "sandbox-api";
}
