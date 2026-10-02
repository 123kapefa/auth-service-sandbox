namespace AuthService.Api.Configuration;

/// <summary>
/// Админка доступа (frontend/, React) на /admin: сборка лежит в wwwroot/admin (в образе её собирает Dockerfile).
/// Адреса разделов (/admin/users…) — маршруты самой админки: на них отдаётся её index.html. CSP строгая: скрипты и
/// стили — только свои файлы, API — тот же origin. Исключение — тег &lt;style&gt;, который вставляют уведомления (sonner):
/// разрешён по хэшу содержимого (версия sonner закреплена в frontend/package.json; обновили — новый хэш подскажет
/// консоль браузера, а оформление и так придёт из собранного CSS).
/// </summary>
public static class AdminUiExtensions
{
    public const string PATH = "/admin";

    // sonner 2.0.8: стили уведомлений и пустой тег до их вставки.
    private const string SONNER_STYLES =
        "'sha256-StEaX+se6YS7pqjzrzMIA0KaX9zF/8zAhvQXZAe5epY=' 'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='";

    private const string CONTENT_SECURITY_POLICY =
        "default-src 'self'; script-src 'self'; style-src 'self' " + SONNER_STYLES + "; img-src 'self' data:; connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";

    /// <summary>Заголовки безопасности для страниц и файлов админки — до раздачи файлов.</summary>
    public static WebApplication UseAdminUiSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(PATH, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.ContentSecurityPolicy = CONTENT_SECURITY_POLICY;
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "same-origin";
            }

            await next(context);
        });
        return app;
    }

    /// <summary>Маршруты админки без файла — её index.html (если админка собрана).</summary>
    public static WebApplication MapAdminUi(this WebApplication app)
    {
        app.MapFallbackToFile($"{PATH}/{{*path:nonfile}}", "admin/index.html");
        return app;
    }
}
