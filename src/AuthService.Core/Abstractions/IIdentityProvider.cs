using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Abstractions;

/// <summary>Сервер входа (Keycloak) глазами сервиса: вход по коду с PKCE, обновление и завершение сессии.</summary>
public interface IIdentityProvider
{
    /// <summary>Куда отправить браузер на вход; state и code verifier нужно сохранить до возврата.</summary>
    Task<Result<AuthorizationRequest, Error>> CreateAuthorizationRequestAsync(Uri redirectUri, CancellationToken cancellationToken);

    /// <summary>Обмен кода из callback на токены.</summary>
    Task<Result<TokenSet, Error>> ExchangeCodeAsync(string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken);

    /// <summary>Новые токены по refresh-токену. Старый refresh-токен после этого недействителен (ротация).</summary>
    Task<Result<TokenSet, Error>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Завершение сессии на сервере входа: refresh-токен и SSO-сессия больше не работают.</summary>
    Task<UnitResult<Error>> LogoutAsync(string refreshToken, CancellationToken cancellationToken);
}

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
