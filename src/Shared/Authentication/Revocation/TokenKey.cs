namespace PlatformAuth.Revocation;

/// <summary>По каким ключам искать отметки для токена.</summary>
/// <param name="Subject">Пользователь (sub).</param>
/// <param name="SessionId">Сессия сервера входа (sid).</param>
/// <param name="AuthorizedParty">Приложение, которому выдан токен (azp).</param>
/// <param name="RolesClient">Клиент ролей сервиса (его audience): роли и права приложения — его роли.</param>
/// <param name="ActiveMode">Активный режим — роль, чьи права в токене.</param>
public sealed record TokenKey(string Subject, string? SessionId, string? AuthorizedParty, string? RolesClient, string? ActiveMode);
