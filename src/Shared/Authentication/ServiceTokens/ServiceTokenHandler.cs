using System.Net;
using System.Net.Http.Headers;

namespace PlatformAuth.ServiceTokens;

/// <summary>
/// Токен сервиса — в каждый исходящий запрос (Authorization: Bearer). 401 — один раз берёт новый токен и повторяет:
/// так переживается перевыпуск ключа и сужение прав (старые токены отзываются мгновенно). Перед повтором — ждёт начала
/// следующей секунды: отметка отзыва включает свою секунду, и токен, взятый в ту же секунду, тоже был бы отклонён.
/// </summary>
public sealed class ServiceTokenHandler(IServiceTokenProvider tokens, TimeProvider timeProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Тело — в буфер: при повторе его отправляют ещё раз (поток читается один раз).
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken);
        }

        string token = await tokens.GetTokenAsync(cancellationToken);
        HttpResponseMessage response = await SendWithAsync(request, token, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        tokens.Invalidate(token);
        response.Dispose();
        DateTimeOffset now = timeProvider.GetUtcNow();
        await Task.Delay(TimeSpan.FromMilliseconds(1000 - now.Millisecond + 10), timeProvider, cancellationToken);
        return await SendWithAsync(request, await tokens.GetTokenAsync(cancellationToken), cancellationToken);
    }

    private Task<HttpResponseMessage> SendWithAsync(HttpRequestMessage request, string token, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, cancellationToken);
    }
}
