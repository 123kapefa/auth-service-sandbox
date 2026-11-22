using Microsoft.AspNetCore.Http;

namespace PlatformAuth.Telemetry;

/// <summary>Кладёт идентификатор трассы в заголовок X-Trace-Id каждого ответа — и успешного, и с ошибкой, и 403 от защиты.</summary>
public sealed class TraceIdMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        // Заголовок ставится при начале ответа: так он есть и у ответов, которые пишут другие middleware.
        context.Response.OnStarting(
            static state =>
            {
                var http = (HttpContext)state;
                if (TraceContext.CurrentId(http) is { } id)
                {
                    http.Response.Headers[TraceContext.RESPONSE_HEADER] = id;
                }

                return Task.CompletedTask;
            },
            context);
        return next(context);
    }
}
