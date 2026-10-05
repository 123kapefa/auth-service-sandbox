using AuthService.Contracts.Services;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace AuthService.Client.AppCatalog;

/// <summary>
/// Отправляет каталог приложения в сервис входа при старте сервиса (в фоне — старт не ждёт). Сервис входа ещё не
/// поднялся или недоступен — повтор каждые 5 секунд, до 2 минут. Ошибка в самом каталоге или в ключе (нет права,
/// код ключа не совпадает с приложением, приложение не подключено) — повторять бесполезно: одна ошибка в лог с
/// причиной. Каталог объявляется на каждом старте — это безопасно: сделанное пропускается.
/// </summary>
internal sealed partial class AppCatalogPublisher(
    DeclareCatalogRequest catalog,
    IAuthServiceClient authService,
    TimeProvider timeProvider,
    ILogger<AppCatalogPublisher> logger) : BackgroundService
{
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    internal const int MAX_ATTEMPTS = 24;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (int attempt = 1; attempt <= MAX_ATTEMPTS && !stoppingToken.IsCancellationRequested; attempt++)
        {
            Result<DeclareCatalogResponse, Error> declared = await authService.DeclareCatalogAsync(catalog, stoppingToken);
            if (declared.IsSuccess)
            {
                Report(declared.Value);
                return;
            }

            Error error = declared.Error;
            if (error.Behavior != ErrorBehavior.Transient)
            {
                LogRejected(logger, error.Messages[0].Code, error.Messages[0].Message);
                return;
            }

            LogRetry(logger, attempt, error.Messages[0].Message);
            await Task.Delay(RetryDelay, timeProvider, stoppingToken);
        }

        LogGaveUp(logger);
    }

    private void Report(DeclareCatalogResponse result)
    {
        LogDeclared(logger, result.App, string.Join(", ", result.CreatedPermissions), string.Join(", ", result.CreatedRoles),
            string.Join(", ", result.KeptRoles));
        if (result.Orphans.Count > 0)
        {
            LogOrphans(logger, result.App, string.Join(", ", result.Orphans));
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "App catalog of {App} declared: new permissions [{Permissions}], new roles [{Roles}], roles kept as the admin set them [{Kept}]")]
    private static partial void LogDeclared(ILogger logger, string app, string permissions, string roles, string kept);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Permissions of {App} declared in code earlier are no longer in the catalog and were kept: [{Orphans}]")]
    private static partial void LogOrphans(ILogger logger, string app, string orphans);

    [LoggerMessage(Level = LogLevel.Error, Message = "The auth service rejected the app catalog ({Code}): {Message}")]
    private static partial void LogRejected(ILogger logger, string code, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "App catalog declaration attempt {Attempt} failed: {Reason}")]
    private static partial void LogRetry(ILogger logger, int attempt, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "App catalog declaration gave up: the auth service is unavailable")]
    private static partial void LogGaveUp(ILogger logger);
}
