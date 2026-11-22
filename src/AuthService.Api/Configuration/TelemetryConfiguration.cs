using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AuthService.Api.Configuration;

/// <summary>
/// Сквозная трассировка (OpenTelemetry): запрос браузера → сервис входа → Keycloak. Сервис создаёт span на каждый запрос и на каждый
/// запрос к Keycloak и передаёт Keycloak заголовок <c>traceparent</c> (W3C), поэтому в логах сервиса и Keycloak один и тот же
/// идентификатор трассы — по нему находятся записи обеих сторон. Идентификатор есть в логах (область TraceId), в заголовке ответа
/// X-Trace-Id и в теле ответа с ошибкой (error.traceId). Если задан <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, spans уходят в коллектор
/// (Jaeger, Tempo, OpenTelemetry Collector) по OTLP; без него идентификаторы есть, а spans нигде не копятся.
/// </summary>
public static class TelemetryConfiguration
{
    public const string SERVICE_NAME = "auth-service";

    public static IServiceCollection AddAuthServiceTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        // Только W3C traceparent и tracestate: baggage от внешнего клиента не нужен и не пересылается в Keycloak.
        Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());

        bool export = !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            || !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"]);

        // Каждая запись журнала внутри запроса несёт TraceId и SpanId (в JSON — в «Scopes»): по ним находятся записи сервиса и Keycloak.
        services.Configure<LoggerFactoryOptions>(options =>
            options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(SERVICE_NAME))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(options => options.Filter = IsTraced);
                tracing.AddHttpClientInstrumentation();
                if (export)
                {
                    tracing.AddOtlpExporter();
                }
            });

        return services;
    }

    private static readonly Action<ILogger, string, string, int, long, Exception?> _requestCompleted = LoggerMessage.Define<string, string, int, long>(
        LogLevel.Information,
        new EventId(1, "RequestCompleted"),
        "HTTP {Method} {Route} -> {Status} in {ElapsedMs} ms");

    private static readonly Action<ILogger, string, string, int, long, Exception?> _requestFailed = LoggerMessage.Define<string, string, int, long>(
        LogLevel.Warning,
        new EventId(2, "RequestFailed"),
        "HTTP {Method} {Route} -> {Status} in {ElapsedMs} ms");

    /// <summary>
    /// Журнал запроса: область (код проекта из realm в Authentication:Authority; идентификаторы запроса и трассы добавляет ASP.NET Core)
    /// и одна строка на запрос — метод, шаблон маршрута, статус и время. Шаблон маршрута, а не адрес: без строки запроса (в ней коды входа)
    /// и без идентификаторов в пути. Здоровье, метрики и статические файлы не пишутся (статика без ошибки — не событие). Ответ 5xx — Warning.
    /// </summary>
    public static IApplicationBuilder UseAuthServiceLogScope(this IApplicationBuilder app)
    {
        string project = ProjectOf(app.ApplicationServices.GetRequiredService<IConfiguration>()["Authentication:Authority"]);
        ILogger logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("AuthService.Request");
        var scope = new ProjectLogScope(project);
        return app.Use(async (context, next) =>
        {
            long started = Stopwatch.GetTimestamp();
            using (logger.BeginScope(scope))
            {
                try
                {
                    await next(context);
                }
                catch (Exception)
                {
                    // Необработанное исключение: статус 500 Kestrel выставит позже, а запись должна сказать правду. Само исключение пишет ASP.NET Core.
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    Log(logger, context, Stopwatch.GetElapsedTime(started));
                    throw;
                }

                Log(logger, context, Stopwatch.GetElapsedTime(started));
            }
        });
    }

    private static void Log(ILogger logger, HttpContext context, TimeSpan elapsed)
    {
        if (!IsTraced(context))
        {
            return;
        }

        string? route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        int status = context.Response.StatusCode;

        // Без маршрута пишем только то, что важно для безопасности и эксплуатации: отказ в доступе (401, 403, 429) и сбой (5xx). Статика и 404
        // от сканеров — не событие: иначе объём журнала задавал бы внешний клиент.
        bool interesting = status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status429TooManyRequests
            || status >= StatusCodes.Status500InternalServerError;
        if (route is null && !interesting)
        {
            return;
        }

        (status >= StatusCodes.Status500InternalServerError ? _requestFailed : _requestCompleted)(
            logger, context.Request.Method, route ?? "(no route)", status, (long)elapsed.TotalMilliseconds, null);
    }

    // Код проекта — последний сегмент адреса realm: https://id.shop.example.com/realms/shop → shop.
    internal static string ProjectOf(string? authority) =>
        string.IsNullOrWhiteSpace(authority) ? "unknown" : authority.TrimEnd('/').Split('/')[^1];

    // Проверки здоровья и сбор метрик каждые несколько секунд засорили бы трассы: их не трассируем.
    private static bool IsTraced(HttpContext context) =>
        !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        && !context.Request.Path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase);

    // Область журнала с полем Project: JSON-журнал пишет её как {"Message":"Project:shop","Project":"shop"}, а не названием типа.
    private sealed class ProjectLogScope(string project) : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly KeyValuePair<string, object>[] _pairs = [new("Project", project)];

        public int Count => _pairs.Length;

        public KeyValuePair<string, object> this[int index] => _pairs[index];

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object>>)_pairs).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _pairs.GetEnumerator();

        public override string ToString() => "Project:" + project;
    }
}
