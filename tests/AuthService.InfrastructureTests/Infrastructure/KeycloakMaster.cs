using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AuthService.InfrastructureTests.Infrastructure;

/// <summary>
/// Мастер-админка Keycloak для подготовки данных тестов: то, что сервис входа сам не делает (завести пользователя с
/// паролем, выдать разрешение на регистрацию клиентов) или что нужно проверить в обход него.
/// </summary>
public sealed class KeycloakMaster(Uri keycloak, string realm, string username, string password) : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = keycloak };

    /// <summary>Пользователь с паролем; возвращает id. Имя — уникальное, с префиксом теста.</summary>
    public async Task<Guid> CreateUserAsync(string username, string password, string? email = null)
    {
        var user = new JsonObject
        {
            ["username"] = username,
            ["enabled"] = true,
            ["email"] = email,
            ["emailVerified"] = email is not null,
            ["credentials"] = new JsonArray(new JsonObject
            {
                ["type"] = "password",
                ["value"] = password,
                ["temporary"] = false,
            }),
        };
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/users", user);
        await EnsureAsync(response);
        return Guid.Parse(response.Headers.Location!.Segments[^1]);
    }

    public async Task DeleteUserAsync(Guid userId)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, $"admin/realms/{realm}/users/{userId}");
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureAsync(response);
        }
    }

    /// <summary>Клиент по clientId (null — нет такого).</summary>
    public async Task<JsonObject?> FindClientAsync(string clientId)
    {
        using HttpResponseMessage response = await SendAsync(
            HttpMethod.Get, $"admin/realms/{realm}/clients?clientId={Uri.EscapeDataString(clientId)}");
        await EnsureAsync(response);
        JsonArray? clients = await response.Content.ReadFromJsonAsync<JsonArray>();
        return clients is { Count: > 0 } ? clients[0]!.AsObject() : null;
    }

    public async Task DeleteClientAsync(string clientId)
    {
        if (await FindClientAsync(clientId) is { } client)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, $"admin/realms/{realm}/clients/{client["id"]}");
            await EnsureAsync(response);
        }
    }

    /// <summary>Разрешение на регистрацию клиентов (initial access token) — как scripts/keycloak/registration-token.sh.</summary>
    public async Task<string> CreateRegistrationTokenAsync(int count)
    {
        using HttpResponseMessage response = await SendAsync(
            HttpMethod.Post, $"admin/realms/{realm}/clients-initial-access", new JsonObject { ["count"] = count, ["expiration"] = 0 });
        await EnsureAsync(response);
        JsonObject? body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return body!["token"]!.GetValue<string>();
    }

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _http.SendAsync(request);
    }

    // Токен живёт минуту — берём новый на каждый запрос: запросов в тестах немного.
    private async Task<string> TokenAsync()
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = username,
            ["password"] = password,
        });
        using HttpResponseMessage response = await _http.PostAsync(new Uri("realms/master/protocol/openid-connect/token", UriKind.Relative), form);
        await EnsureAsync(response);
        JsonObject? body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return body!["access_token"]!.GetValue<string>();
    }

    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Keycloak admin: {(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: "
                + await response.Content.ReadAsStringAsync());
        }
    }
}
