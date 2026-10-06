using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace AuthService.Infrastructure.Keycloak;

/// <summary>
/// Уведомления через плагин Keycloak platform-account-notice ({realm}/platform-account-notice/users/{id}): письмо по
/// шаблону темы писем или SMS телефонией плагина. Токен служебной учётки — нужна роль manage-users (realm-management).
/// </summary>
internal sealed partial class KeycloakAccountNotices(
    HttpClient http,
    KeycloakServiceAccount serviceAccount,
    ILogger<KeycloakAccountNotices> logger) : IAccountNotices
{
    private const string PATH = "platform-account-notice/users/";

    public async Task<Result<string, Error>> SignInMethodsChangedAsync(
        Guid userId,
        SignInMethodNotice notice,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        Result<Uri, Error> realm = await serviceAccount.GetRealmBaseAsync(cancellationToken);
        if (realm.IsFailure)
        {
            return realm.Error;
        }

        Result<string, Error> token = await serviceAccount.GetTokenAsync(cancellationToken);
        if (token.IsFailure)
        {
            return token.Error;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(realm.Value, PATH + userId))
        {
            Content = JsonContent.Create(new NoticeRepresentation(notice.Event, notice.Method)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogRejected(logger, (int)response.StatusCode);
                return response.StatusCode == HttpStatusCode.NotFound
                    ? IdentityProviderErrors.UserNotFound()
                    : IdentityProviderErrors.Unavailable();
            }

            SentRepresentation? sent = await response.Content.ReadFromJsonAsync<SentRepresentation>(cancellationToken);
            return sent?.Sent ?? "none";
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }
        catch (JsonException exception)
        {
            LogUnreachable(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak is unreachable for account notices")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak refused the account notice: {StatusCode} (no platform-account-notice plugin or no manage-users role?)")]
    private static partial void LogRejected(ILogger logger, int statusCode);

    private sealed record NoticeRepresentation(
        [property: JsonPropertyName("event")] string Event,
        [property: JsonPropertyName("method")] string? Method);

    private sealed record SentRepresentation([property: JsonPropertyName("sent")] string? Sent);
}
