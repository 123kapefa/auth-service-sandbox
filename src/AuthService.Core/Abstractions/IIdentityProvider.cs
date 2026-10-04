using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Abstractions;

/// <summary>Сервер входа (Keycloak) глазами сервиса: вход по коду с PKCE, обновление и завершение сессии.</summary>
public interface IIdentityProvider
{
    /// <summary>
    /// Куда отправить браузер на вход; state и code verifier нужно сохранить до возврата.
    /// forceLogin — войти заново даже при живой SSO-сессии (повторный вход перед опасным действием);
    /// action — после входа показать страницу действия с учётной записью (пароль, почта, 2FA, ключи доступа);
    /// app — приложение, в которое входят (страницы входа и письма — в его оформлении); null — проект.
    /// </summary>
    Task<Result<AuthorizationRequest, Error>> CreateAuthorizationRequestAsync(
        Uri redirectUri,
        bool forceLogin,
        AccountActionRequest? action,
        string? app,
        CancellationToken cancellationToken);

    /// <summary>Обмен кода из callback на токены.</summary>
    Task<Result<TokenSet, Error>> ExchangeCodeAsync(string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken);

    /// <summary>Новые токены по refresh-токену. Старый refresh-токен после этого недействителен (ротация).</summary>
    Task<Result<TokenSet, Error>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Завершение сессии на сервере входа: refresh-токен и SSO-сессия больше не работают.</summary>
    Task<UnitResult<Error>> LogoutAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Проверка logout_token (OIDC back-channel logout): подпись ключами realm, издатель, адресат — наш клиент,
    /// событие backchannel-logout. Сервер входа сообщает так о сессиях, завершённых у него самого.
    /// </summary>
    Task<Result<LogoutNotice, Error>> ValidateLogoutTokenAsync(string logoutToken, CancellationToken cancellationToken);
}

/// <summary>Какая сессия завершена на сервере входа: sid (если есть) и пользователь.</summary>
public sealed record LogoutNotice(string? Subject, string? SessionId);

/// <summary>Начало входа: куда отправить браузер и что запомнить до его возврата.</summary>
/// <param name="Url">Адрес страницы входа со всеми параметрами.</param>
/// <param name="State">Защита от подделки callback: должен вернуться тем же.</param>
/// <param name="CodeVerifier">Секрет PKCE: без него украденный код не обменять на токены.</param>
public sealed record AuthorizationRequest(Uri Url, string State, string CodeVerifier);

/// <summary>Токены сервера входа и моменты их истечения.</summary>
public sealed record TokenSet(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
