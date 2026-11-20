namespace AuthService.Api.Configuration;

/// <summary>
/// Демо-страница разработчика (/dev/: сессия, режимы, проверка запросов) нужна только на стенде и в разработке. В бою её
/// нет: она не защищена CSP интерфейсов и показывает внутреннее устройство. Включается явно настройкой DemoPage:Enabled
/// (appsettings.Development.json и appsettings.Docker.json); без неё /dev/ отвечает 404. В Production включить нельзя —
/// сервис не стартует. Модуль входа для страниц приложений (/sdk/) демо-страницей не является и доступен всегда.
/// </summary>
public static class DemoPageConfiguration
{
    public const string ENABLED_KEY = "DemoPage:Enabled";
    public const string PATH = "/dev";

    public static bool IsEnabled(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        bool enabled = configuration.GetValue(ENABLED_KEY, false);
        if (enabled && string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ENABLED_KEY} нельзя включать в Production: демо-страница разработчика только для стенда и разработки. Уберите настройку.");
        }

        return enabled;
    }

    /// <summary>Закрывает /dev/ (404) до раздачи файлов, если демо-страница не включена.</summary>
    public static WebApplication CloseDemoPageUnlessEnabled(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (IsEnabled(app.Configuration, app.Environment.EnvironmentName))
        {
            return app;
        }

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(PATH, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
        return app;
    }
}
