namespace AuthService.Contracts.Auth;

/// <summary>Параметры, с которыми сервер входа возвращает браузер в /auth/callback.</summary>
public sealed record CallbackRequest(string? Code, string? State, string? Error, string? ErrorDescription);
