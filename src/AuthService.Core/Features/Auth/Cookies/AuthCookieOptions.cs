using Microsoft.AspNetCore.Http;

namespace AuthService.Core.Features.Auth.Cookies;

/// <summary>Секция Cookies: как ставятся cookie с токенами (имена — PlatformAuth.AuthCookies).</summary>
public sealed class AuthCookieOptions
{
    public const string SECTION_NAME = "Cookies";

    /// <summary>
    /// Родительский домен (.example.com), чтобы вход видели все поддомены. Пусто — cookie только для текущего хоста.
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>Только по https. false — локально (http).</summary>
    public bool Secure { get; init; } = true;

    /// <summary>Lax — cookie уходит при переходе по ссылке с другого поддомена; Strict ломает такой вход.</summary>
    public SameSiteMode SameSite { get; init; } = SameSiteMode.Lax;
}
