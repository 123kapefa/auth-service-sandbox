using Microsoft.AspNetCore.Http;

namespace AuthService.Core.Features.Auth.Cookies;

/// <summary>Секция Cookies: как ставятся cookie с токенами (имена — PlatformAuth.AuthCookies).</summary>
public sealed class AuthCookieOptions
{
    public const string SECTION_NAME = "Cookies";

    // Domain нет намеренно: cookie с токенами только для хоста сервиса входа. Родительский домен отправлял бы
    // refresh-токен (Path=/auth/) и access-токен на любой поддомен проекта — любое приложение получило бы сессию человека.
    // Приложения проекта входят сами (PKCE, /sdk/platform-login.js) и носят токен в заголовке Authorization.

    /// <summary>Только по https. false — локально (http).</summary>
    public bool Secure { get; init; } = true;

    /// <summary>Lax — cookie уходит при переходе по ссылке с другого поддомена; Strict ломает такой вход.</summary>
    public SameSiteMode SameSite { get; init; } = SameSiteMode.Lax;
}
