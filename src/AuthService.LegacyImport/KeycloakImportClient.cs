using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AuthService.LegacyImport;

/// <summary>
/// Запись в Keycloak через partialImport (только он сохраняет Id пользователя) от имени временного клиента legacy-import.
/// ifResourceExists=SKIP: повторный прогон ничего не дублирует и не перезаписывает.
/// </summary>
public sealed class KeycloakImportClient(HttpClient http, KeycloakTarget target)
{
    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    /// <summary>Пачка целиком; если Keycloak отклонил её (например, Id уже занят под другим логином) — по одному.</summary>
    public async Task<IReadOnlyList<ImportOutcome>> ImportAsync(IReadOnlyList<UserRepresentation> users, CancellationToken cancellationToken)
    {
        (PartialImportResponse? response, string? error) = await PartialImportAsync(users, cancellationToken);
        if (response is not null)
        {
            return Classify(users, response);
        }

        if (users.Count == 1)
        {
            return [ImportOutcome.Failed(users[0], error ?? "неизвестная ошибка")];
        }

        List<ImportOutcome> outcomes = [];
        foreach (UserRepresentation user in users)
        {
            outcomes.AddRange(await ImportAsync([user], cancellationToken));
        }

        return outcomes;
    }

    internal static IReadOnlyList<ImportOutcome> Classify(IReadOnlyList<UserRepresentation> users, PartialImportResponse response)
    {
        Dictionary<string, PartialImportResult> byName = response.Results
            .Where(result => string.Equals(result.ResourceType, "USER", StringComparison.Ordinal))
            .ToDictionary(result => result.ResourceName, StringComparer.Ordinal);

        return [.. users.Select(user => byName.TryGetValue(user.Username, out PartialImportResult? result)
            ? Classify(user, result)
            : ImportOutcome.Failed(user, "нет в ответе Keycloak"))];
    }

    private static ImportOutcome Classify(UserRepresentation user, PartialImportResult result) => result.Action switch
    {
        "ADDED" => ImportOutcome.Added(user),
        // Пропущен: логин уже есть. Тот же Id — перенесён раньше; другой — номер занят другим пользователем Keycloak.
        "SKIPPED" when Guid.TryParse(result.Id, out Guid existing) && existing == user.Id => ImportOutcome.AlreadyImported(user),
        "SKIPPED" => ImportOutcome.Conflict(user, result.Id),
        _ => ImportOutcome.Failed(user, $"действие Keycloak «{result.Action}»"),
    };

    private async Task<(PartialImportResponse? Response, string? Error)> PartialImportAsync(
        IReadOnlyList<UserRepresentation> users,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(target.BaseUrl, $"admin/realms/{target.Realm}/partialImport"))
        {
            Content = JsonContent.Create(new PartialImportRequest("SKIP", users)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken));

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<PartialImportResponse>(cancellationToken), null);
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return (null, $"{(int)response.StatusCode}: {body}");
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _token;
        }

        using FormUrlEncodedContent form = new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = target.ClientId,
            ["client_secret"] = target.ClientSecret,
        });
        using HttpResponseMessage response = await http.PostAsync(
            new Uri(target.BaseUrl, $"realms/{target.Realm}/protocol/openid-connect/token"), form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Keycloak не выдал токен клиенту {target.ClientId} ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        TokenResponse token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Пустой ответ на запрос токена");
        _token = token.AccessToken;
        _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 30);
        return _token;
    }

    private sealed record PartialImportRequest(
        [property: JsonPropertyName("ifResourceExists")] string IfResourceExists,
        [property: JsonPropertyName("users")] IReadOnlyList<UserRepresentation> Users);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

public sealed record PartialImportResponse(
    [property: JsonPropertyName("added")] int Added,
    [property: JsonPropertyName("skipped")] int Skipped,
    [property: JsonPropertyName("results")] IReadOnlyList<PartialImportResult> Results);

public sealed record PartialImportResult(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("resourceName")] string ResourceName,
    [property: JsonPropertyName("id")] string Id);

public enum ImportStatus
{
    Added,
    AlreadyImported,
    Conflict,
    Failed,
}

/// <summary>Что стало с пользователем: перенесён, уже был, конфликт (номер занят другим Id) или ошибка.</summary>
public sealed record ImportOutcome(UserRepresentation User, ImportStatus Status, string? Detail)
{
    public static ImportOutcome Added(UserRepresentation user) => new(user, ImportStatus.Added, null);

    public static ImportOutcome AlreadyImported(UserRepresentation user) => new(user, ImportStatus.AlreadyImported, null);

    public static ImportOutcome Conflict(UserRepresentation user, string existingId) =>
        new(user, ImportStatus.Conflict, $"номер занят пользователем Keycloak {existingId}");

    public static ImportOutcome Failed(UserRepresentation user, string error) => new(user, ImportStatus.Failed, error);
}
