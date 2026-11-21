using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PlatformAuth;

namespace AuthService.Core.Features.Auth.Cookies;

/// <summary>Что нужно помнить между уходом на страницу входа и возвратом в callback; Action — действие с учётной записью.</summary>
public sealed record LoginState(string State, string CodeVerifier, string ReturnTo, string? Action = null);

/// <summary>
/// Состояние входа — в зашифрованной одноразовой cookie на 10 минут: сервер ничего не хранит,
/// подделать или прочитать её клиент не может.
/// </summary>
public sealed class LoginStateStore(IDataProtectionProvider dataProtection, IOptions<AuthCookieOptions> options, TimeProvider timeProvider)
{
    /// <summary>Имя без приставки (локально по http); по https — <see cref="CookieName"/>.</summary>
    public const string COOKIE_NAME = "auth_login";

    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("AuthService.LoginState.v1");
    private readonly AuthCookieOptions _options = options.Value;

    /// <summary>По https — <c>__Host-auth_login</c> (Secure, Path=/, без Domain): состояние входа не подменить с соседнего поддомена.</summary>
    public string CookieName => _options.Secure ? AuthCookies.HOST_PREFIX + COOKIE_NAME : COOKIE_NAME;

    public void Save(HttpResponse response, LoginState state)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Append(
            CookieName,
            _protector.Protect(JsonSerializer.Serialize(state)),
            Build(timeProvider.GetUtcNow().Add(_lifetime)));
    }

    /// <summary>Читает и сразу удаляет: повторно использовать состояние входа нельзя.</summary>
    public LoginState? Take(HttpRequest request, HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(CookieName, Build(expires: null));
        if (!request.Cookies.TryGetValue(CookieName, out string? payload) || string.IsNullOrEmpty(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LoginState>(_protector.Unprotect(payload));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null; // подделана, повреждена или зашифрована ключом до перезапуска
        }
    }

    // SameSite=Lax обязателен: cookie должна прийти в callback, куда браузер возвращается со страницы Keycloak.
    private CookieOptions Build(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = _options.Secure,
        SameSite = SameSiteMode.Lax,
        Path = _options.Secure ? "/" : AuthCookieWriter.AUTH_PATH, // __Host- требует Path=/
        Expires = expires,
    };
}
