using AuthService.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PlatformAuth;

namespace AuthService.Core.Features.Auth.Cookies;

/// <summary>
/// Cookie с токенами по контракту. По https (Cookies:Secure=true) имена с приставкой <c>__Host-</c>
/// (<c>__Host-accessToken</c>, <c>__Host-refreshToken</c>): оба Path=/, без Domain — браузер не даст подменить их с соседнего
/// поддомена. Локально по http (Secure невозможна) — простые <c>accessToken</c> (Path=/) и <c>refreshToken</c> (Path=/auth/).
/// </summary>
public sealed class AuthCookieWriter(IOptions<AuthCookieOptions> options)
{
    public const string AUTH_PATH = "/auth/";

    private readonly AuthCookieOptions _options = options.Value;

    /// <summary>Имя cookie с access-токеном — и имя, которое сервис ждёт в Authentication:AccessTokenCookie.</summary>
    public string AccessTokenName => AuthCookies.AccessToken(_options.Secure);

    /// <summary>Имя cookie с refresh-токеном.</summary>
    public string RefreshTokenName => AuthCookies.RefreshToken(_options.Secure);

    // __Host- требует Path=/, поэтому refresh-cookie по https не сужена до /auth/; хост сервиса входа отдельный, а токен
    // защищён httpOnly, Secure и SameSite.
    private string RefreshPath => _options.Secure ? "/" : AUTH_PATH;

    public void SetTokens(HttpResponse response, TokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(tokens);

        // Cookie живёт столько же, сколько токен в ней: просроченный JWT браузер никуда не отправит.
        response.Cookies.Append(AccessTokenName, tokens.AccessToken, Build("/", tokens.AccessTokenExpiresAt));
        response.Cookies.Append(RefreshTokenName, tokens.RefreshToken, Build(RefreshPath, tokens.RefreshTokenExpiresAt));
        DeleteLegacyNames(response);
    }

    public void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Path — тот же, что при установке, иначе браузер не удалит cookie. Domain нет: cookie только для этого хоста.
        // Имена без приставки (до перехода на https-имена) тоже снимаем: иначе старая cookie лежала бы в браузере до срока.
        response.Cookies.Delete(AccessTokenName, Build("/", expires: null));
        response.Cookies.Delete(RefreshTokenName, Build(RefreshPath, expires: null));
        DeleteLegacyNames(response);
    }

    // После перехода на https-имена в браузере остаются accessToken и refreshToken без приставки (до 30 суток): снимаем при
    // следующем входе или выходе, чтобы не лежали мёртвым грузом и не уходили на /auth/ вместе с запросами.
    private void DeleteLegacyNames(HttpResponse response)
    {
        if (_options.Secure)
        {
            response.Cookies.Delete(AuthCookies.ACCESS_TOKEN, Build("/", expires: null));
            response.Cookies.Delete(AuthCookies.REFRESH_TOKEN, Build(AUTH_PATH, expires: null));
        }
    }

    public string? ReadRefreshToken(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies.TryGetValue(RefreshTokenName, out string? token) && !string.IsNullOrWhiteSpace(token)
            ? token
            : null;
    }

    /// <summary>Access-токен из cookie запроса (до обновления) — для сравнения с новым.</summary>
    public string? ReadAccessToken(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies.TryGetValue(AccessTokenName, out string? token) ? token : null;
    }

    private CookieOptions Build(string path, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = _options.Secure,
        SameSite = _options.SameSite,
        Path = path,
        Expires = expires,
    };
}
