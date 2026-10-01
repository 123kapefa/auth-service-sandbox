using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace AuthService.Infrastructure.Keycloak;

/// <summary>
/// Admin API Keycloak от имени служебной учётки клиента сервиса. Браузер к Admin API не ходит:
/// права проверяет наш сервис, Keycloak видит только служебную учётку.
/// </summary>
internal sealed partial class KeycloakAdminClient(
    HttpClient httpClient,
    KeycloakServiceAccount serviceAccount,
    ILogger<KeycloakAdminClient> logger) : IIdentityAdmin
{
    public async Task<Result<IdentityUser, Error>> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<HttpResponseMessage, Error> response = await SendAsync(HttpMethod.Get, $"users/{userId}", cancellationToken);
        if (response.IsFailure)
        {
            return response.Error;
        }

        using HttpResponseMessage message = response.Value;
        if (message.StatusCode == HttpStatusCode.NotFound)
        {
            return IdentityProviderErrors.UserNotFound();
        }

        if (!message.IsSuccessStatusCode)
        {
            LogUnexpectedResponse(logger, (int)message.StatusCode);
            return IdentityProviderErrors.Unavailable();
        }

        UserRepresentation? user = await message.Content.ReadFromJsonAsync<UserRepresentation>(cancellationToken);
        return user is null ? IdentityProviderErrors.Unavailable() : ToIdentityUser(user);
    }

    public async Task<Result<IReadOnlyList<string>, Error>> GetClientRolesAsync(
        Guid userId,
        string clientId,
        CancellationToken cancellationToken)
    {
        // Список ролей по клиентам приходит с clientId приложения — без поиска клиента (на это нужны лишние права).
        Result<RoleMappings, Error> mappings = await GetJsonAsync<RoleMappings>($"users/{userId}/role-mappings", cancellationToken);
        if (mappings.IsFailure)
        {
            return mappings.Error;
        }

        IReadOnlyList<string> roles = mappings.Value.ClientMappings is not null
            && mappings.Value.ClientMappings.TryGetValue(clientId, out ClientMapping? client)
                ? [.. client.Mappings.Select(role => role.Name)]
                : [];
        return Result.Success<IReadOnlyList<string>, Error>(roles);
    }

    public async Task<Result<bool, Error>> HasPasswordAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<Credential[], Error> credentials = await GetJsonAsync<Credential[]>($"users/{userId}/credentials", cancellationToken);
        return credentials.IsSuccess
            ? credentials.Value.Any(credential => string.Equals(credential.Type, "password", StringComparison.Ordinal))
            : credentials.Error;
    }

    public async Task<UnitResult<Error>> SetAttributeAsync(
        Guid userId,
        string name,
        string value,
        CancellationToken cancellationToken)
    {
        // PUT заменяет атрибуты целиком: читаем пользователя полностью, меняем один атрибут, пишем обратно.
        Result<JsonObject, Error> user = await GetJsonAsync<JsonObject>($"users/{userId}", cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        JsonObject attributes = user.Value["attributes"] as JsonObject ?? [];
        attributes[name] = new JsonArray(value);
        user.Value["attributes"] = attributes;

        Result<HttpResponseMessage, Error> response = await SendAsync(HttpMethod.Put, $"users/{userId}", cancellationToken, JsonContent.Create(user.Value));
        if (response.IsFailure)
        {
            return response.Error;
        }

        using HttpResponseMessage message = response.Value;
        return message.IsSuccessStatusCode ? UnitResult.Success<Error>() : ToError(message);
    }

    private async Task<Result<T, Error>> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        Result<HttpResponseMessage, Error> response = await SendAsync(HttpMethod.Get, path, cancellationToken);
        if (response.IsFailure)
        {
            return response.Error;
        }

        using HttpResponseMessage message = response.Value;
        if (!message.IsSuccessStatusCode)
        {
            return ToError(message);
        }

        T? body = await message.Content.ReadFromJsonAsync<T>(cancellationToken);
        return body is null ? IdentityProviderErrors.Unavailable() : body;
    }

    // 404 у пользовательских путей Admin API — нет такого пользователя; остальное — сбой Keycloak.
    private Error ToError(HttpResponseMessage message)
    {
        if (message.StatusCode == HttpStatusCode.NotFound)
        {
            return IdentityProviderErrors.UserNotFound();
        }

        LogUnexpectedResponse(logger, (int)message.StatusCode);
        return IdentityProviderErrors.Unavailable();
    }

    private async Task<Result<HttpResponseMessage, Error>> SendAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        HttpContent? content = null)
    {
        Result<Uri, Error> adminBase = await serviceAccount.GetAdminBaseAsync(cancellationToken);
        if (adminBase.IsFailure)
        {
            return adminBase.Error;
        }

        Result<string, Error> token = await serviceAccount.GetTokenAsync(cancellationToken);
        if (token.IsFailure)
        {
            return token.Error;
        }

        using var request = new HttpRequestMessage(method, new Uri(adminBase.Value, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }
    }

    private static IdentityUser ToIdentityUser(UserRepresentation user) =>
        new(
            user.Id,
            user.Username,
            user.Email,
            Attribute(user, "phone"),
            Attribute(user, "active_mode"),
            user.Enabled,
            DateTimeOffset.FromUnixTimeMilliseconds(user.CreatedTimestamp));

    private static string? Attribute(UserRepresentation user, string name) =>
        user.Attributes is not null && user.Attributes.TryGetValue(name, out string[]? values) && values.Length > 0 ? values[0] : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak Admin API is unreachable")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unexpected Keycloak Admin API response: {StatusCode}")]
    private static partial void LogUnexpectedResponse(ILogger logger, int statusCode);

    private sealed record RoleMappings(
        [property: JsonPropertyName("clientMappings")] Dictionary<string, ClientMapping>? ClientMappings);

    private sealed record ClientMapping([property: JsonPropertyName("mappings")] RoleRepresentation[] Mappings);

    private sealed record RoleRepresentation([property: JsonPropertyName("name")] string Name);

    private sealed record Credential([property: JsonPropertyName("type")] string Type);

    // Представление пользователя Admin API (UserRepresentation) — только нужные поля.
    private sealed record UserRepresentation(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("createdTimestamp")] long CreatedTimestamp,
        [property: JsonPropertyName("attributes")] Dictionary<string, string[]>? Attributes);
}
