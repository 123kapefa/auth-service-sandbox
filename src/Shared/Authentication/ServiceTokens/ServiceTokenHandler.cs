using System.Net;
using System.Net.Http.Headers;

namespace PlatformAuth.ServiceTokens;

/// <summary>
/// Токен сервиса — в каждый исходящий запрос (Authorization: Bearer). 401 — один раз берёт новый токен и повторяет:
/// так переживается перевыпуск ключа и сужение прав (старые токены отзываются мгновенно).
/// </summary>
public sealed class ServiceTokenHandler(IServiceTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string token = await tokens.GetTokenAsync(cancellationToken);
        HttpResponseMessage response = await SendWithAsync(request, token, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        tokens.Invalidate(token);
        response.Dispose();
        return await SendWithAsync(request, await tokens.GetTokenAsync(cancellationToken), cancellationToken);
    }

    private Task<HttpResponseMessage> SendWithAsync(HttpRequestMessage request, string token, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, cancellationToken);
    }
}
