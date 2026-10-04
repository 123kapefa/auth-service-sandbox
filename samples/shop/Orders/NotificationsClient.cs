using System.Net.Http.Json;
using PlatformAuth.ServiceTokens;

namespace Orders;

/// <summary>
/// «Уведомления» — по ключу «Заказов»: токен сервиса ставит ServiceTokenHandler (AddServiceToken), на 401 берёт новый
/// и повторяет. Ответ — что стало с уведомлением, для показа в заказе; заказ от него не зависит.
/// </summary>
internal sealed partial class NotificationsClient(HttpClient http, ILogger<NotificationsClient> logger)
{
    public async Task<string> SendAsync(Guid userId, string text, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.PostAsJsonAsync("api/notifications", new { userId, text }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogRejected(logger, (int)response.StatusCode);
                return $"не отправлено: «Уведомления» ответили {(int)response.StatusCode}";
            }

            Sent? sent = await response.Content.ReadFromJsonAsync<Sent>(cancellationToken);
            return sent?.Delivery ?? "отправлено";
        }
        catch (ServiceTokenException exception)
        {
            // Ключ «Заказов» отозван или в настройках старый секрет.
            LogKeyRejected(logger, exception);
            return "не отправлено: сервис входа не принял ключ «Заказов»";
        }
        catch (HttpRequestException exception)
        {
            LogUnavailable(logger, exception);
            return "не отправлено: «Уведомления» недоступны";
        }
    }

    private sealed record Sent(string Delivery);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notifications rejected the request: {StatusCode}")]
    private static partial void LogRejected(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "The orders service key was rejected by the auth server")]
    private static partial void LogKeyRejected(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notifications are unavailable")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);
}
