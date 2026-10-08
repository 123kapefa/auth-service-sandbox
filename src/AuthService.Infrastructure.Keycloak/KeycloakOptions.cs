using Microsoft.Extensions.Options;

namespace AuthService.Infrastructure.Keycloak;

/// <summary>Секция Keycloak: клиент, от имени которого сервис ведёт вход. Адреса realm — из секции Authentication.</summary>
public sealed class KeycloakOptions
{
    public const string SECTION_NAME = "Keycloak";

    public string ClientId { get; init; } = string.Empty;

    /// <summary>Секрет confidential-клиента. Только из переменной окружения / хранилища секретов.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// Прежний секрет — только на время смены секрета (docs/secrets.md): Keycloak отверг ClientSecret — запрос
    /// повторяется с этим. Пусто — повтора нет.
    /// </summary>
    public string PreviousClientSecret { get; init; } = string.Empty;

    public string Scope { get; init; } = "openid";

    /// <summary>
    /// Разрешение на регистрацию клиентов (initial access token) — выдаёт техник (scripts/keycloak/registration-token.sh).
    /// Без него выдача ключей сервисам выключена. Только из переменной окружения / хранилища секретов.
    /// </summary>
    public string RegistrationToken { get; init; } = string.Empty;
}

internal sealed class KeycloakOptionsValidator : IValidateOptions<KeycloakOptions>
{
    public ValidateOptionsResult Validate(string? name, KeycloakOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Keycloak:ClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("Keycloak:ClientSecret is required (set Keycloak__ClientSecret).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
