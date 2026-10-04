namespace AuthService.Contracts.Me;

/// <summary>Безопасность в кабинете: чем пользователь входит и что подключено для второго шага.</summary>
/// <param name="HasPassword">Пароль задан.</param>
/// <param name="Authenticators">Приложения-аутентификаторы (2FA).</param>
/// <param name="Passkeys">Ключи доступа (вход без пароля).</param>
/// <param name="RecoveryCodes">Коды восстановления, если созданы.</param>
public sealed record SecurityResponse(
    bool HasPassword,
    IReadOnlyList<CredentialResponse> Authenticators,
    IReadOnlyList<CredentialResponse> Passkeys,
    RecoveryCodesResponse? RecoveryCodes);

/// <summary>Подключённое приложение или устройство: Id (для удаления), подпись, когда подключено.</summary>
public sealed record CredentialResponse(string Id, string? Label, DateTimeOffset CreatedAt);

/// <summary>Коды восстановления: Id (для удаления), когда созданы, сколько осталось из скольких.</summary>
public sealed record RecoveryCodesResponse(string Id, DateTimeOffset CreatedAt, int? Remaining, int? Total);
