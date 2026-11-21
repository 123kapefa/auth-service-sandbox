using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace PlatformAuth.Csrf;

/// <summary>
/// Защита от CSRF собственным заголовком. Запрос, который меняет данные (не GET/HEAD/OPTIONS/TRACE) и опирается на cookie
/// с токеном, должен нести заголовок <see cref="HEADER_NAME"/>: форма или запрос с чужой страницы такой заголовок поставить не
/// могут (для этого браузеру нужен CORS-предзапрос, а сервисы чужие источники не пускают). Это второй рубеж рядом с
/// SameSite=Lax: он держит, даже если cookie когда-либо снова станет общей для поддоменов и страница на взломанном поддомене
/// отправит POST без тела (перевыпуск ключа, сброс пароля, выход). Запросы с заголовком Authorization и запросы без cookie с
/// токеном (сервис — сервису по ключу, обратный вызов Keycloak) не затрагиваются: подделывать нечего.
/// </summary>
public sealed class CsrfHeaderMiddleware(RequestDelegate next, IOptions<JwtAuthenticationOptions> jwt)
{
    /// <summary>Заголовок, без которого меняющий запрос с cookie токена отклоняется. Значение — любое непустое.</summary>
    public const string HEADER_NAME = "X-Requested-With";

    /// <summary>Код ошибки 403.</summary>
    public const string ERROR_CODE = "auth.csrf_header_required";

    // Все имена cookie с токенами: с приставкой __Host- (https) и без (локально) — проверка не зависит от настройки — плюс
    // то имя, под которым сервис сам читает access-токен (Authentication:AccessTokenCookie), если оно своё.
    private readonly string[] _tokenCookies =
    [
        AuthCookies.AccessToken(hostPrefix: true),
        AuthCookies.RefreshToken(hostPrefix: true),
        AuthCookies.ACCESS_TOKEN,
        AuthCookies.REFRESH_TOKEN,
        .. string.IsNullOrWhiteSpace(jwt.Value.AccessTokenCookie) ? [] : new[] { jwt.Value.AccessTokenCookie },
    ];

    public Task InvokeAsync(HttpContext context)
    {
        if (!IsSafeMethod(context.Request.Method)
            && !context.Request.Headers.ContainsKey(HeaderNames.Authorization)
            && _tokenCookies.Any(name => context.Request.Cookies.ContainsKey(name))
            && !context.Request.Headers[HEADER_NAME].Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            return Reject(context);
        }

        return next(context);
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);

    private static Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(
            new
            {
                result = (object?)null,
                error = new
                {
                    messages = new[]
                    {
                        new
                        {
                            code = ERROR_CODE,
                            message = $"Запрос, меняющий данные, должен нести заголовок {HEADER_NAME}.",
                            invalidField = (string?)null,
                        },
                    },
                    type = "AUTHORIZATION",
                },
                isError = true,
            },
            context.RequestAborted);
    }
}

/// <summary>Ставит <see cref="CsrfHeaderMiddleware"/> в начало конвейера каждого сервиса, подключившего проверку токенов.</summary>
internal sealed class CsrfHeaderStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<CsrfHeaderMiddleware>();
        next(app);
    };
}
