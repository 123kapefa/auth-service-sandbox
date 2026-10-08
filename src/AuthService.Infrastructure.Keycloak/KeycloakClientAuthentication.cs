using System.Net;
using System.Text.Json;

namespace AuthService.Infrastructure.Keycloak;

/// <summary>
/// Запрос к token/logout endpoint от имени клиента сервиса: client_id и client_secret в теле формы (client_secret_post).
/// Смена секрета без потери сессий (docs/secrets.md): сервис запускают с новым секретом в ClientSecret и старым в
/// PreviousClientSecret, затем меняют секрет в Keycloak. Пока Keycloak помнит старый, новый отвергается
/// (401 unauthorized_client) — запрос повторяется со старым; после смены в Keycloak сразу проходит новый.
/// </summary>
internal static class KeycloakClientAuthentication
{
    public static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient httpClient,
        Uri endpoint,
        IReadOnlyDictionary<string, string> form,
        KeycloakOptions options,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await PostAsync(httpClient, endpoint, form, options.ClientId, options.ClientSecret, cancellationToken);
        if (string.IsNullOrWhiteSpace(options.PreviousClientSecret) || !await IsClientRejectedAsync(response, cancellationToken))
        {
            return response;
        }

        response.Dispose();
        return await PostAsync(httpClient, endpoint, form, options.ClientId, options.PreviousClientSecret, cancellationToken);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient httpClient,
        Uri endpoint,
        IReadOnlyDictionary<string, string> form,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> body = new(form, StringComparer.Ordinal)
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        };
        using var content = new FormUrlEncodedContent(body);
        return await httpClient.PostAsync(endpoint, content, cancellationToken);
    }

    // Keycloak отвечает на неверный секрет 401 unauthorized_client; invalid_client — по RFC 6749.
    private static async Task<bool> IsClientRejectedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest))
        {
            return false;
        }

        // Тело читается строкой из буфера, не потоком: поток HttpContent отдаёт один раз, а если повтора не будет,
        // вызывающий прочитает тело ещё раз.
        await response.Content.LoadIntoBufferAsync(cancellationToken);
        try
        {
            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return json.RootElement.ValueKind == JsonValueKind.Object
                   && json.RootElement.TryGetProperty("error", out JsonElement error)
                   && error.GetString() is "unauthorized_client" or "invalid_client";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
