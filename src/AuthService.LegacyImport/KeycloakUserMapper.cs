using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AuthService.LegacyImport;

/// <summary>
/// Пользователь боевого сервиса → представление Keycloak для partialImport. Правила — как у сервиса входа песочницы:
/// логин — телефон цифрами, атрибуты phone и active_mode, роли сотрудников — роли клиента приложения, блокировка —
/// enabled=false с причиной. Пароль — тот же BCrypt-хэш с algorithm="bcrypt" (проверяет плагин Keycloak).
/// Роль по умолчанию (default-roles-{realm}, в ней customer) — явно: partialImport, в отличие от создания пользователя,
/// её не выдаёт.
/// </summary>
public static partial class KeycloakUserMapper
{
    public const string BCRYPT = "bcrypt";

    /// <summary>Роли боевого сервиса, которые есть в песочнице как роли клиента приложения (customer — у всех по умолчанию).</summary>
    private static readonly HashSet<string> _staffRoles = new(StringComparer.Ordinal) { "manager", "admin" };

    public static MappingResult Map(LegacyUser user, KeycloakTarget target)
    {
        string username = user.Phone.TrimStart('+');
        if (!PhoneDigits().IsMatch(username))
        {
            return MappingResult.Rejected(user, $"телефон «{user.Phone}» — не номер E.164");
        }

        List<string> notes = [];
        string role = user.Role;
        Dictionary<string, string[]> attributes = new(StringComparer.Ordinal)
        {
            ["phone"] = [user.Phone],
            ["legacy_login_count"] = [user.LoginCount.ToString(CultureInfo.InvariantCulture)],
        };

        if (string.Equals(role, "customer", StringComparison.Ordinal) || _staffRoles.Contains(role))
        {
            attributes["active_mode"] = [role];
        }
        else
        {
            // Роли нет в песочнице (courier) — покупатель; исходная роль — в атрибуте, решение — за админом.
            attributes["active_mode"] = ["customer"];
            attributes["legacy_role"] = [role];
            notes.Add($"роль «{role}» не переносится — покупатель, исходная роль в атрибуте legacy_role");
        }

        if (user.LastLoginAtUtc is { } lastLogin)
        {
            attributes["legacy_last_login_at"] = [Iso(lastLogin)];
        }

        if (user.IsRestricted)
        {
            attributes["restriction_reason"] = [user.RestrictionReason ?? string.Empty];
            if (user.RestrictedAtUtc is { } restrictedAt)
            {
                attributes["restricted_at"] = [Iso(restrictedAt)];
            }
        }

        List<CredentialRepresentation> credentials = [];
        if (user.PasswordHash is null)
        {
            notes.Add("без пароля — вход по коду");
        }
        else if (BcryptHash().Match(user.PasswordHash) is { Success: true } hash)
        {
            credentials.Add(new CredentialRepresentation(
                "password",
                JsonSerializer.Serialize(new SecretData(user.PasswordHash, string.Empty)),
                JsonSerializer.Serialize(new CredentialData(int.Parse(hash.Groups["cost"].ValueSpan, CultureInfo.InvariantCulture), BCRYPT)),
                user.PasswordSetAtUtc?.ToUnixTimeMilliseconds()));
        }
        else
        {
            notes.Add("пароль не в формате BCrypt — перенесён без пароля");
        }

        Dictionary<string, string[]>? clientRoles = _staffRoles.Contains(role)
            ? new Dictionary<string, string[]>(StringComparer.Ordinal) { [target.RolesClientId] = [role] }
            : null;

        return MappingResult.Mapped(user, new UserRepresentation(
            user.Id,
            username,
            !user.IsRestricted,
            user.CreatedAt.ToUnixTimeMilliseconds(),
            attributes,
            credentials,
            [$"default-roles-{target.Realm}"],
            clientRoles), notes);
    }

    private static string Iso(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    [GeneratedRegex("^[0-9]{10,15}$")]
    private static partial Regex PhoneDigits();

    // Как в плагине (BcryptHash): $2a$/$2b$/$2y$, стоимость, 53 символа соли и хэша.
    [GeneratedRegex(@"^\$2[aby]\$(?<cost>[0-9]{2})\$[./A-Za-z0-9]{53}$")]
    private static partial Regex BcryptHash();

    private sealed record SecretData(
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("salt")] string Salt);

    private sealed record CredentialData(
        [property: JsonPropertyName("hashIterations")] int HashIterations,
        [property: JsonPropertyName("algorithm")] string Algorithm);
}

/// <summary>Итог сопоставления: представление для Keycloak или причина, по которой пользователь не переносится.</summary>
public sealed record MappingResult(LegacyUser Source, UserRepresentation? User, string? RejectReason, IReadOnlyList<string> Notes)
{
    public static MappingResult Mapped(LegacyUser source, UserRepresentation user, IReadOnlyList<string> notes) => new(source, user, null, notes);

    public static MappingResult Rejected(LegacyUser source, string reason) => new(source, null, reason, []);
}

public sealed record UserRepresentation(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("createdTimestamp")] long CreatedTimestamp,
    [property: JsonPropertyName("attributes")] Dictionary<string, string[]> Attributes,
    [property: JsonPropertyName("credentials")] IReadOnlyList<CredentialRepresentation> Credentials,
    [property: JsonPropertyName("realmRoles")] IReadOnlyList<string> RealmRoles,
    [property: JsonPropertyName("clientRoles"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, string[]>? ClientRoles);

public sealed record CredentialRepresentation(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("secretData")] string SecretData,
    [property: JsonPropertyName("credentialData")] string CredentialData,
    [property: JsonPropertyName("createdDate"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CreatedDate);
