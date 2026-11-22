using System.Diagnostics;
using AuthService.Core;

namespace AuthService.Infrastructure.Keycloak;

/// <summary>
/// Считает запросы сервиса к Keycloak: куда (токены, ключи, выход, Admin API, регистрация клиентов) и чем кончилось (класс
/// статуса или ошибка соединения), и время ответа. Метки короткие и конечные: ни путей, ни realm, ни тел запросов.
/// </summary>
internal sealed class KeycloakMetricsHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        string target = Target(request.RequestUri);
        try
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            AuthMetrics.KeycloakRequested(target, StatusClass((int)response.StatusCode), Stopwatch.GetElapsedTime(started).TotalSeconds);
            return response;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            // Нет соединения или таймаут — сбой Keycloak. Запрос, отменённый самим клиентом (человек закрыл вкладку), сюда не
            // попадает: это не сбой Keycloak, и оповещения по ошибкам не должны на него срабатывать.
            AuthMetrics.KeycloakRequested(target, "error", Stopwatch.GetElapsedTime(started).TotalSeconds);
            throw;
        }
    }

    private static string StatusClass(int status) => status switch
    {
        >= 500 => "5xx",
        >= 400 => "4xx",
        >= 300 => "3xx",
        _ => "2xx",
    };

    // Что за запрос — по пути, без realm и идентификаторов.
    private static string Target(Uri? uri)
    {
        string path = uri?.AbsolutePath ?? string.Empty;
        return path switch
        {
            _ when path.Contains("/protocol/openid-connect/token", StringComparison.Ordinal) => "token",
            _ when path.Contains("/protocol/openid-connect/logout", StringComparison.Ordinal) => "logout",
            _ when path.Contains("/protocol/openid-connect/certs", StringComparison.Ordinal)
                || path.Contains("/.well-known/", StringComparison.Ordinal) => "keys",
            _ when path.Contains("/clients-registrations/", StringComparison.Ordinal) => "registration",
            _ when path.StartsWith("/admin/", StringComparison.Ordinal) => "admin",
            _ => "other",
        };
    }
}
