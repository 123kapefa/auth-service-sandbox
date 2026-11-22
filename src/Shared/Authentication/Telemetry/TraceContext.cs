using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace PlatformAuth.Telemetry;

/// <summary>Идентификатор трассы текущего запроса — по нему находятся записи сервиса и Keycloak.</summary>
public static class TraceContext
{
    /// <summary>Заголовок ответа с идентификатором трассы: клиент называет его в обращении в поддержку.</summary>
    public const string RESPONSE_HEADER = "X-Trace-Id";

    /// <summary>
    /// Идентификатор трассы W3C (32 hex-символа) текущего запроса; нет трассировки — null (внутренний идентификатор запроса Kestrel вида
    /// «0HN…:00000001» наружу не отдаём: он раскрывает соединение и счётчик запросов). Не секрет: ни токена, ни данных человека в нём нет.
    /// </summary>
    public static string? CurrentId(HttpContext? context) => Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.TraceId.ToString() : null;
}
