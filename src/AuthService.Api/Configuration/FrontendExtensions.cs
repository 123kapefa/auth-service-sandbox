namespace AuthService.Api.Configuration;

/// <summary>
/// Интерфейсы сервиса входа (frontend/, React): админка доступа на /admin и личный кабинет на /account. Сборки лежат в
/// wwwroot/admin и wwwroot/account (в образе их собирает Dockerfile). Адреса разделов (/admin/users, /account/security…)
/// — маршруты самих приложений: на них отдаётся их index.html. CSP строгая: скрипты и стили — только свои файлы, API —
/// тот же origin. Исключение — тег &lt;style&gt;, который вставляют уведомления (sonner): разрешён по хэшу содержимого
/// (версия sonner закреплена в frontend/package.json; обновили — новый хэш подскажет консоль браузера, а оформление и
/// так придёт из собранного CSS).
/// </summary>
public static class FrontendExtensions
{
    public const string ADMIN_PATH = "/admin";
    public const string ACCOUNT_PATH = "/account";

    private static readonly string[] _paths = [ADMIN_PATH, ACCOUNT_PATH];

    // sonner 2.0.8: стили уведомлений и пустой тег до их вставки.
    private const string SONNER_STYLES =
        "'sha256-StEaX+se6YS7pqjzrzMIA0KaX9zF/8zAhvQXZAe5epY=' 'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='";

    private const string CONTENT_SECURITY_POLICY =
        "default-src 'self'; script-src 'self'; style-src 'self' " + SONNER_STYLES + "; img-src 'self' data:; connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";

    /// <summary>Заголовки безопасности для страниц и файлов интерфейсов — до раздачи файлов.</summary>
    public static WebApplication UseFrontendSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (Array.Exists(_paths, path => context.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.Headers.ContentSecurityPolicy = CONTENT_SECURITY_POLICY;
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "same-origin";
            }

            await next(context);
        });
        return app;
    }

    /// <summary>
    /// Маршруты интерфейсов без файла — их index.html (если интерфейс собран). Корень сервиса — личный кабинет: туда
    /// попадает пользователь, открывший адрес сервиса или вышедший; демо-страница для разработчика — на /dev/.
    /// </summary>
    public static WebApplication MapFrontends(this WebApplication app)
    {
        app.MapGet("/", () => Results.Redirect($"{ACCOUNT_PATH}/"));
        app.MapFallbackToFile($"{ADMIN_PATH}/{{*path:nonfile}}", "admin/index.html");
        app.MapFallbackToFile($"{ACCOUNT_PATH}/{{*path:nonfile}}", "account/index.html");
        return app;
    }
}
