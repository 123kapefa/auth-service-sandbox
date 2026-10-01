using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    private async Task<Result<HttpResponseMessage, Error>> SendAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
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

        using var request = new HttpRequestMessage(method, new Uri(adminBase.Value, path));
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

    // Представление пользователя Admin API (UserRepresentation) — только нужные поля.
    private sealed record UserRepresentation(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("createdTimestamp")] long CreatedTimestamp,
        [property: JsonPropertyName("attributes")] Dictionary<string, string[]>? Attributes);
}
