using AuthService.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PlatformAuth;

namespace AuthService.Core.Features.Auth.Cookies;

/// <summary>
/// Cookie с токенами по контракту: accessToken — всем сервисам (Path=/), refreshToken — только эндпоинтам входа (Path=/auth/).
/// </summary>
public sealed class AuthCookieWriter(IOptions<AuthCookieOptions> options)
{
    public const string AUTH_PATH = "/auth/";

    private readonly AuthCookieOptions _options = options.Value;

    public void SetTokens(HttpResponse response, TokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(tokens);

        // Cookie живёт столько же, сколько токен в ней: просроченный JWT браузер никуда не отправит.
        response.Cookies.Append(AuthCookies.ACCESS_TOKEN, tokens.AccessToken, Build("/", tokens.AccessTokenExpiresAt));
        response.Cookies.Append(AuthCookies.REFRESH_TOKEN, tokens.RefreshToken, Build(AUTH_PATH, tokens.RefreshTokenExpiresAt));
    }

    public void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Domain и Path — те же, что при установке, иначе браузер не удалит cookie.
        response.Cookies.Delete(AuthCookies.ACCESS_TOKEN, Build("/", expires: null));
        response.Cookies.Delete(AuthCookies.REFRESH_TOKEN, Build(AUTH_PATH, expires: null));
    }

    public static string? ReadRefreshToken(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies.TryGetValue(AuthCookies.REFRESH_TOKEN, out string? token) && !string.IsNullOrWhiteSpace(token)
            ? token
            : null;
    }

    private CookieOptions Build(string path, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = _options.Secure,
        SameSite = _options.SameSite,
        Domain = string.IsNullOrWhiteSpace(_options.Domain) ? null : _options.Domain,
        Path = path,
        Expires = expires,
    };
}
