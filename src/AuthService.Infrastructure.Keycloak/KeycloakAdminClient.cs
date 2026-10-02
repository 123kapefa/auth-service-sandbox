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

    public async Task<Result<IdentityUser?, Error>> FindUserExactAsync(string login, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        string trimmed = login.Trim();
        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [trimmed.Contains('@', StringComparison.Ordinal) ? "email" : "username"] = trimmed,
            ["exact"] = "true",
            ["briefRepresentation"] = "false",
        };
        Result<UserRepresentation[], Error> users =
            await GetJsonAsync<UserRepresentation[]>(QueryHelpers.AddQueryString("users", query), cancellationToken);
        if (users.IsFailure)
        {
            return users.Error;
        }

        return users.Value.Length == 1 ? ToIdentityUser(users.Value[0]) : (IdentityUser?)null;
    }

    public async Task<Result<IReadOnlyList<IdentityUser>, Error>> GetRoleUsersAsync(
        string clientId,
        string roleName,
        int first,
        int max,
        CancellationToken cancellationToken)
    {
        Result<string, Error> client = await FindClientIdAsync(clientId, cancellationToken);
        if (client.IsFailure)
        {
            return IdentityProviderErrors.AppNotFound();
        }

        string path = QueryHelpers.AddQueryString(
            $"clients/{client.Value}/roles/{Uri.EscapeDataString(roleName)}/users",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["first"] = first.ToString(CultureInfo.InvariantCulture),
                ["max"] = max.ToString(CultureInfo.InvariantCulture),
                ["briefRepresentation"] = "false",
            });
        Result<UserRepresentation[], Error> users = await GetJsonAsync<UserRepresentation[]>(path, cancellationToken);
        return users.IsSuccess
            ? Result.Success<IReadOnlyList<IdentityUser>, Error>([.. users.Value.Select(ToIdentityUser)])
            : users.Error;
    }

    public async Task<Result<IReadOnlyList<ClientInfo>, Error>> GetClientsAsync(CancellationToken cancellationToken)
    {
        // Служебная учётка видит только клиенты, на которые ей дано право (FGAP v2).
        Result<ClientRepresentation[], Error> clients = await GetJsonAsync<ClientRepresentation[]>("clients", cancellationToken);
        return clients.IsSuccess
            ? Result.Success<IReadOnlyList<ClientInfo>, Error>(
                [.. clients.Value.OrderBy(client => client.ClientId, StringComparer.Ordinal).Select(client => new ClientInfo(client.ClientId, DisplayName(client)))])
            : clients.Error;
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

    public async Task<Result<IReadOnlyList<string>, Error>> GetEffectiveClientRolesAsync(
        Guid userId,
        string clientId,
        CancellationToken cancellationToken)
    {
        Result<string, Error> client = await FindClientIdAsync(clientId, cancellationToken);
        if (client.IsFailure)
        {
            return IdentityProviderErrors.AppNotFound();
        }

        Result<RoleRepresentation[], Error> roles = await GetJsonAsync<RoleRepresentation[]>(
            $"users/{userId}/role-mappings/clients/{client.Value}/composite", cancellationToken);
        return roles.IsFailure
            ? roles.Error
            : Result.Success<IReadOnlyList<string>, Error>([.. roles.Value.Select(role => role.Name)]);
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
                apps.Add(new AppAccessInfo(client.ClientId, DisplayName(client), !denied));
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

    public async Task<Result<IReadOnlyList<AppRole>, Error>> GetAppRolesAsync(string clientId, CancellationToken cancellationToken)
    {
        Result<string, Error> client = await FindClientIdAsync(clientId, cancellationToken);
        if (client.IsFailure)
        {
            return IdentityProviderErrors.AppNotFound();
        }

        Result<RoleRepresentation[], Error> roles = await GetJsonAsync<RoleRepresentation[]>(
            $"clients/{client.Value}/roles?briefRepresentation=false", cancellationToken);
        return roles.IsFailure
            ? roles.Error
            : Result.Success<IReadOnlyList<AppRole>, Error>([.. roles.Value.Select(ToAppRole).OrderBy(role => role.Name, StringComparer.Ordinal)]);
    }

    public async Task<UnitResult<Error>> CreateAppRoleAsync(string clientId, AppRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);

        Result<string, Error> client = await FindClientIdAsync(clientId, cancellationToken);
        if (client.IsFailure)
        {
            return IdentityProviderErrors.AppNotFound();
        }

        Result<HttpResponseMessage, Error> response = await SendAsync(
            HttpMethod.Post, $"clients/{client.Value}/roles", cancellationToken, JsonContent.Create(ToRepresentation(role)));
        if (response.IsFailure)
        {
            return response.Error;
        }

        using HttpResponseMessage message = response.Value;
        return message.StatusCode == HttpStatusCode.Conflict
            ? IdentityProviderErrors.RoleExists(role.Name)
            : message.IsSuccessStatusCode ? UnitResult.Success<Error>() : ToError(message);
    }

    public async Task<UnitResult<Error>> UpdateAppRoleAsync(string clientId, AppRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);

        Result<string, Error> client = await FindClientIdAsync(clientId, cancellationToken);
        return client.IsFailure
            ? IdentityProviderErrors.AppNotFound()
            : await SendExpectingSuccessAsync(
                HttpMethod.Put,
                $"clients/{client.Value}/roles/{Uri.EscapeDataString(role.Name)}",
                cancellationToken,
                JsonContent.Create(ToRepresentation(role)));
    }

    public async Task<UnitResult<Error>> DeleteAppRoleAsync(string clientId, string name, CancellationToken cancellationToken)
    {
        Result<string, Error> client = await FindClientIdAsync(clientId, cancellationToken);
        return client.IsFailure
            ? IdentityProviderErrors.AppNotFound()
            : await SendExpectingSuccessAsync(HttpMethod.Delete, $"clients/{client.Value}/roles/{Uri.EscapeDataString(name)}", cancellationToken);
    }

    public async Task<Result<IReadOnlyList<string>, Error>> GetRoleCompositesAsync(
        string clientId,
        string roleName,
        CancellationToken cancellationToken)
    {
        Result<string, Error> client = await FindClientIdAsync(clientId, cancellationToken);
        if (client.IsFailure)
        {
            return IdentityProviderErrors.AppNotFound();
        }

        Result<RoleRepresentation[], Error> composites = await GetJsonAsync<RoleRepresentation[]>(
            $"clients/{client.Value}/roles/{Uri.EscapeDataString(roleName)}/composites", cancellationToken);
        return composites.IsFailure
            ? composites.Error
            : Result.Success<IReadOnlyList<string>, Error>([.. composites.Value.Select(role => role.Name).Order(StringComparer.Ordinal)]);
    }

    public async Task<UnitResult<Error>> SetRoleCompositesAsync(
        string clientId,
        string roleName,
        IReadOnlyCollection<string> add,
        IReadOnlyCollection<string> remove,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);

        Result<string, Error> client = await FindClientIdAsync(clientId, cancellationToken);
        if (client.IsFailure)
        {
            return IdentityProviderErrors.AppNotFound();
        }

        // Состав меняется по id и имени ролей: берём их из списка ролей клиента.
        Result<RoleRepresentation[], Error> roles = await GetJsonAsync<RoleRepresentation[]>($"clients/{client.Value}/roles", cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        string path = $"clients/{client.Value}/roles/{Uri.EscapeDataString(roleName)}/composites";
        foreach ((HttpMethod method, IReadOnlyCollection<string> names) in new[] { (HttpMethod.Post, add), (HttpMethod.Delete, remove) })
        {
            RoleRepresentation[] selected = [.. roles.Value.Where(role => names.Contains(role.Name)).Select(role => new RoleRepresentation(role.Id, role.Name))];
            if (selected.Length == 0)
            {
                continue;
            }

            UnitResult<Error> changed = await SendExpectingSuccessAsync(method, path, cancellationToken, JsonContent.Create(selected));
            if (changed.IsFailure)
            {
                return changed;
            }
        }

        return UnitResult.Success<Error>();
    }

    // Встроенные клиенты Keycloak названы ключом перевода (${client_account-console}) — показывать его нельзя.
    private static string? DisplayName(ClientRepresentation client) =>
        client.Name is { } name && !name.StartsWith("${", StringComparison.Ordinal) ? name : null;

    private static AppRole ToAppRole(RoleRepresentation role) =>
        new(
            Guid.TryParse(role.Id, out Guid id) ? id : null,
            role.Name,
            role.Description,
            First(role.Attributes, AppRoleKinds.ATTRIBUTE_KIND) ?? AppRoleKinds.ROLE,
            First(role.Attributes, AppRoleKinds.ATTRIBUTE_SOURCE),
            string.Equals(First(role.Attributes, AppRoleKinds.ATTRIBUTE_BUILTIN), "true", StringComparison.Ordinal),
            long.TryParse(First(role.Attributes, AppRoleKinds.ATTRIBUTE_VERSION), NumberStyles.Integer, CultureInfo.InvariantCulture, out long version)
                ? version
                : null);

    private static Dictionary<string, object?> ToRepresentation(AppRole role)
    {
        Dictionary<string, string[]> attributes = new(StringComparer.Ordinal) { [AppRoleKinds.ATTRIBUTE_KIND] = [role.Kind] };
        if (role.Source is not null)
        {
            attributes[AppRoleKinds.ATTRIBUTE_SOURCE] = [role.Source];
        }

        if (role.Builtin)
        {
            attributes[AppRoleKinds.ATTRIBUTE_BUILTIN] = ["true"];
        }

        if (role.Version is { } version)
        {
            attributes[AppRoleKinds.ATTRIBUTE_VERSION] = [version.ToString(CultureInfo.InvariantCulture)];
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = role.Name,
            ["description"] = role.Description,
            ["attributes"] = attributes,
        };
    }

    private static string? First(Dictionary<string, string[]>? attributes, string name) =>
        attributes is not null && attributes.TryGetValue(name, out string[]? values) && values.Length > 0 ? values[0] : null;

    public async Task<Result<IReadOnlyList<string>, Error>> GetCredentialTypesAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<Credential[], Error> credentials = await GetJsonAsync<Credential[]>($"users/{userId}/credentials", cancellationToken);
        return credentials.IsSuccess
            ? Result.Success<IReadOnlyList<string>, Error>([.. credentials.Value.Select(credential => credential.Type).Distinct(StringComparer.Ordinal)])
            : credentials.Error;
    }

    public async Task<Result<int, Error>> RemoveCredentialsAsync(
        Guid userId,
        IReadOnlyCollection<string> types,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(types);

        Result<Credential[], Error> credentials = await GetJsonAsync<Credential[]>($"users/{userId}/credentials", cancellationToken);
        if (credentials.IsFailure)
        {
            return credentials.Error;
        }

        Credential[] selected = [.. credentials.Value.Where(credential => types.Contains(credential.Type))];
        foreach (Credential credential in selected)
        {
            UnitResult<Error> removed = await SendExpectingSuccessAsync(
                HttpMethod.Delete, $"users/{userId}/credentials/{Uri.EscapeDataString(credential.Id ?? string.Empty)}", cancellationToken);
            if (removed.IsFailure)
            {
                return removed.Error;
            }
        }

        return selected.Length;
    }

    public Task<UnitResult<Error>> SendActionsEmailAsync(
        Guid userId,
        IReadOnlyCollection<string> actions,
        TimeSpan lifespan,
        CancellationToken cancellationToken)
    {
        string path = QueryHelpers.AddQueryString(
            $"users/{userId}/execute-actions-email",
            "lifespan",
            ((int)lifespan.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        return SendExpectingSuccessAsync(HttpMethod.Put, path, cancellationToken, JsonContent.Create(actions));
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
                : null,
            (user.Attributes ?? [])
                .Where(attribute => attribute.Key.StartsWith(UserAttributes.APP_MODE_PREFIX, StringComparison.Ordinal) && attribute.Value.Length > 0)
                .ToDictionary(attribute => attribute.Key[UserAttributes.APP_MODE_PREFIX.Length..], attribute => attribute.Value[0], StringComparer.Ordinal));

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
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("attributes")] Dictionary<string, string[]>? Attributes = null);

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

    private sealed record Credential([property: JsonPropertyName("type")] string Type, [property: JsonPropertyName("id")] string? Id = null);

    // Представление пользователя Admin API (UserRepresentation) — только нужные поля.
    private sealed record UserRepresentation(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("createdTimestamp")] long CreatedTimestamp,
        [property: JsonPropertyName("attributes")] Dictionary<string, string[]>? Attributes);
}
