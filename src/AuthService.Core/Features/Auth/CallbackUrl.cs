using Microsoft.AspNetCore.Http;

namespace AuthService.Core.Features.Auth;

/// <summary>
/// Адрес /auth/callback этого сервиса. Одинаковый при уходе на вход и при обмене кода — так требует OIDC.
/// Схема и хост — из запроса (за прокси их исправляют forwarded headers); Keycloak пускает только адреса из своего списка.
/// </summary>
public static class CallbackUrl
{
    public const string PATH = "/auth/callback";

    public static Uri For(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Uri($"{request.Scheme}://{request.Host}{PATH}");
    }
}
