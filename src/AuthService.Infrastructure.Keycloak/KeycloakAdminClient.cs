using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.WebUtilities;
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

    public async Task<Result<IReadOnlyList<IdentityUser>, Error>> SearchUsersAsync(
        string? search,
        int first,
        int max,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["first"] = first.ToString(CultureInfo.InvariantCulture),
            ["max"] = max.ToString(CultureInfo.InvariantCulture),
            ["briefRepresentation"] = "false", // нужны атрибуты: телефон и режим
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            query["search"] = search.Trim();
        }

        Result<UserRepresentation[], Error> users =
            await GetJsonAsync<UserRepresentation[]>(QueryHelpers.AddQueryString("users", query), cancellationToken);
        return users.IsSuccess
            ? Result.Success<IReadOnlyList<IdentityUser>, Error>([.. users.Value.Select(ToIdentityUser)])
            : users.Error;
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

    public async Task<UnitResult<Error>> SetClientRolesAsync(
        Guid userId,
        string clientId,
        IReadOnlyCollection<string> grant,
        IReadOnlyCollection<string> revoke,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(revoke);

        Result<string, Error> client = await FindClientIdAsync(clientId, cancellationToken);
        if (client.IsFailure)
        {
            return client.Error;
        }

        string path = $"users/{userId}/role-mappings/clients/{client.Value}";
        if (grant.Count > 0)
        {
            // Роль выдаётся по id и имени: берём их из ролей, доступных этому пользователю.
            Result<RoleRepresentation[], Error> available = await GetJsonAsync<RoleRepresentation[]>($"{path}/available", cancellationToken);
            if (available.IsFailure)
            {
                return available.Error;
            }

            UnitResult<Error> granted = await SendExpectingSuccessAsync(
                HttpMethod.Post, path, cancellationToken, JsonContent.Create(available.Value.Where(role => grant.Contains(role.Name))));
            if (granted.IsFailure)
            {
                return granted;
            }
        }

        if (revoke.Count > 0)
        {
            Result<RoleRepresentation[], Error> assigned = await GetJsonAsync<RoleRepresentation[]>(path, cancellationToken);
            if (assigned.IsFailure)
            {
                return assigned.Error;
            }

            return await SendExpectingSuccessAsync(
                HttpMethod.Delete, path, cancellationToken, JsonContent.Create(assigned.Value.Where(role => revoke.Contains(role.Name))));
        }

        return UnitResult.Success<Error>();
    }

    public async Task<Result<IReadOnlyList<AppAccessInfo>, Error>> GetAppAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Служебная учётка видит только клиенты приложений, на которые ей дано право (FGAP v2).
        Result<ClientRepresentation[], Error> clients = await GetJsonAsync<ClientRepresentation[]>("clients", cancellationToken);
        if (clients.IsFailure)
        {
            return clients.Error;
        }

        List<AppAccessInfo> apps = [];
        foreach (ClientRepresentation client in clients.Value.OrderBy(found => found.ClientId, StringComparer.Ordinal))
        {
            string path = $"users/{userId}/role-mappings/clients/{client.Id}";
            Result<RoleRepresentation[], Error> assigned = await GetJsonAsync<RoleRepresentation[]>(path, cancellationToken);
            Result<RoleRepresentation[], Error> available = await GetJsonAsync<RoleRepresentation[]>($"{path}/available", cancellationToken);
            if (assigned.IsFailure || available.IsFailure)
            {
                return assigned.IsFailure ? assigned.Error : available.Error;
            }

            bool denied = assigned.Value.Any(role => string.Equals(role.Name, AppRoles.ACCESS_DENIED, StringComparison.Ordinal));
            if (denied || available.Value.Any(role => string.Equals(role.Name, AppRoles.ACCESS_DENIED, StringComparison.Ordinal)))
            {
                apps.Add(new AppAccessInfo(client.ClientId, client.Name, !denied));
            }
        }

        return apps;
    }

    // Внутренний id клиента по clientId. Служебная учётка видит только клиенты, на которые ей дано право (FGAP v2).
    private async Task<Result<string, Error>> FindClientIdAsync(string clientId, CancellationToken cancellationToken)
    {
        Result<ClientRepresentation[], Error> clients = await GetJsonAsync<ClientRepresentation[]>(
            QueryHelpers.AddQueryString("clients", new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["clientId"] = clientId,
                ["search"] = "true",
            }),
            cancellationToken);
        if (clients.IsFailure)
        {
            return clients.Error;
        }

        ClientRepresentation? client = clients.Value.FirstOrDefault(found => string.Equals(found.ClientId, clientId, StringComparison.Ordinal));
        if (client is null)
        {
            LogClientNotVisible(logger, clientId);
            return IdentityProviderErrors.Unavailable();
        }

        return client.Id;
    }

    public async Task<Result<IReadOnlyList<LoginEvent>, Error>> GetLoginEventsAsync(
        Guid? userId,
        IReadOnlyCollection<string> types,
        int first,
        int max,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(types);

        // type повторяется: events?type=LOGIN&type=LOGIN_ERROR&…
        List<KeyValuePair<string, string?>> query =
        [
            .. types.Select(type => new KeyValuePair<string, string?>("type", type)),
            new("first", first.ToString(CultureInfo.InvariantCulture)),
            new("max", max.ToString(CultureInfo.InvariantCulture)),
        ];
        if (userId is { } id)
        {
            query.Add(new("user", id.ToString()));
        }

        Result<EventRepresentation[], Error> events =
            await GetJsonAsync<EventRepresentation[]>(QueryHelpers.AddQueryString("events", query), cancellationToken);
        return events.IsFailure
            ? events.Error
            : Result.Success<IReadOnlyList<LoginEvent>, Error>(
            [
                .. events.Value.Select(found => new LoginEvent(
                    DateTimeOffset.FromUnixTimeMilliseconds(found.Time),
                    found.Type,
                    Guid.TryParse(found.UserId, out Guid user) ? user : null,
                    found.IpAddress,
                    found.ClientId,
                    found.Error,
                    found.Details?.GetValueOrDefault("username"))),
            ]);
    }

    public async Task<Result<bool, Error>> HasPasswordAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<Credential[], Error> credentials = await GetJsonAsync<Credential[]>($"users/{userId}/credentials", cancellationToken);
        return credentials.IsSuccess
            ? credentials.Value.Any(credential => string.Equals(credential.Type, "password", StringComparison.Ordinal))
            : credentials.Error;
    }

    public async Task<UnitResult<Error>> UpdateUserAsync(Guid userId, UserChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        // PUT заменяет атрибуты целиком: читаем пользователя полностью, меняем нужное, пишем обратно.
        Result<JsonObject, Error> user = await GetJsonAsync<JsonObject>($"users/{userId}", cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        JsonObject attributes = user.Value["attributes"] as JsonObject ?? [];
        foreach ((string name, string? value) in changes.Attributes)
        {
            if (value is null)
            {
                attributes.Remove(name);
            }
            else
            {
                attributes[name] = new JsonArray(value);
            }
        }

        user.Value["attributes"] = attributes;
        if (changes.Enabled is { } enabled)
        {
            user.Value["enabled"] = enabled;
        }

        return await SendExpectingSuccessAsync(HttpMethod.Put, $"users/{userId}", cancellationToken, JsonContent.Create(user.Value));
    }

    public async Task<Result<IReadOnlyList<IdentitySession>, Error>> GetSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<SessionRepresentation[], Error> sessions = await GetJsonAsync<SessionRepresentation[]>($"users/{userId}/sessions", cancellationToken);
        return sessions.IsFailure
            ? sessions.Error
            : Result.Success<IReadOnlyList<IdentitySession>, Error>(
            [
                .. sessions.Value
                    .OrderByDescending(session => session.LastAccess)
                    .Select(session => new IdentitySession(
                        session.Id,
                        session.IpAddress,
                        DateTimeOffset.FromUnixTimeMilliseconds(session.Start),
                        DateTimeOffset.FromUnixTimeMilliseconds(session.LastAccess),
                        [.. (session.Clients?.Values ?? Enumerable.Empty<string>()).Order(StringComparer.Ordinal)])),
            ]);
    }

    public Task<UnitResult<Error>> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        SendExpectingSuccessAsync(HttpMethod.Delete, $"sessions/{Uri.EscapeDataString(sessionId)}", cancellationToken);

    public Task<UnitResult<Error>> LogoutUserAsync(Guid userId, CancellationToken cancellationToken) =>
        SendExpectingSuccessAsync(HttpMethod.Post, $"users/{userId}/logout", cancellationToken);

    private async Task<UnitResult<Error>> SendExpectingSuccessAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        HttpContent? content = null)
    {
        Result<HttpResponseMessage, Error> response = await SendAsync(method, path, cancellationToken, content);
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
            DateTimeOffset.FromUnixTimeMilliseconds(user.CreatedTimestamp),
            Attribute(user, UserAttributes.RESTRICTION_REASON),
            DateTimeOffset.TryParse(Attribute(user, UserAttributes.RESTRICTED_AT), CultureInfo.InvariantCulture, out DateTimeOffset restrictedAt)
                ? restrictedAt
                : null);

    private static string? Attribute(UserRepresentation user, string name) =>
        user.Attributes is not null && user.Attributes.TryGetValue(name, out string[]? values) && values.Length > 0 ? values[0] : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak Admin API is unreachable")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Client {ClientId} is not visible to the service account: grant view and map-roles on it (admin permissions)")]
    private static partial void LogClientNotVisible(ILogger logger, string clientId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unexpected Keycloak Admin API response: {StatusCode}")]
    private static partial void LogUnexpectedResponse(ILogger logger, int statusCode);

    private sealed record RoleMappings(
        [property: JsonPropertyName("clientMappings")] Dictionary<string, ClientMapping>? ClientMappings);

    private sealed record ClientMapping([property: JsonPropertyName("mappings")] RoleRepresentation[] Mappings);

    private sealed record RoleRepresentation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record ClientRepresentation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("clientId")] string ClientId,
        [property: JsonPropertyName("name")] string? Name);

    private sealed record SessionRepresentation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("ipAddress")] string? IpAddress,
        [property: JsonPropertyName("start")] long Start,
        [property: JsonPropertyName("lastAccess")] long LastAccess,
        [property: JsonPropertyName("clients")] Dictionary<string, string>? Clients);

    private sealed record EventRepresentation(
        [property: JsonPropertyName("time")] long Time,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("userId")] string? UserId,
        [property: JsonPropertyName("ipAddress")] string? IpAddress,
        [property: JsonPropertyName("clientId")] string? ClientId,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("details")] Dictionary<string, string>? Details);

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
