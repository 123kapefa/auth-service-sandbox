namespace AuthService.Contracts.Security;

/// <summary>Свои входы за последние 30 дней (события сервера входа), новые сверху.</summary>
public sealed record LoginHistoryResponse(IReadOnlyList<LoginHistoryItem> Items);

/// <summary>Одна попытка входа.</summary>
/// <param name="Time">Когда.</param>
/// <param name="Success">Вошёл (false — ошибка входа).</param>
/// <param name="Error">Причина ошибки от сервера входа: invalid_user_credentials, user_temporarily_disabled…</param>
/// <param name="IpAddress">С какого IP.</param>
/// <param name="DeviceLabel">Устройство («Chrome · macOS»); у неудачной попытки сессии нет — null.</param>
/// <param name="SessionId">Сессия, начатая этим входом (у неудачной — null).</param>
public sealed record LoginHistoryItem(
    DateTimeOffset Time,
    bool Success,
    string? Error,
    string? IpAddress,
    string? DeviceLabel,
    string? SessionId);
