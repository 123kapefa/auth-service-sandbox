using System.Globalization;
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
/// Способы входа проекта через плагин Keycloak platform-sign-in ({realm}/platform-sign-in/methods, …/{способ}/affected):
/// читать и менять — токеном служебной учётки с ролью realm manage-sign-in.
/// </summary>
internal sealed partial class KeycloakSignIn(
    HttpClient http,
    KeycloakServiceAccount serviceAccount,
    ILogger<KeycloakSignIn> logger) : IProjectSignIn
{
    private const string METHODS = "platform-sign-in/methods";

    public async Task<Result<ProjectSignInSettings, Error>> GetAsync(CancellationToken cancellationToken)
    {
        Result<SettingsRepresentation, Error> body = await SendAsync<SettingsRepresentation>(HttpMethod.Get, METHODS, null, cancellationToken);
        return body.Map(ToSettings);
    }

    public async Task<Result<ProjectSignInSettings, Error>> SetAsync(ProjectSignInSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var request = new SettingsRequest(
            settings.Methods.ToDictionary(
                pair => pair.Key,
                pair => new MethodRequest(pair.Value.Status, pair.Value.Until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                StringComparer.Ordinal),
            settings.TwoFactorRequired ? ProjectSignInRules.TWO_FACTOR_REQUIRED : ProjectSignInRules.TWO_FACTOR_OPTIONAL);
        Result<SettingsRepresentation, Error> body = await SendAsync<SettingsRepresentation>(HttpMethod.Put, METHODS, JsonContent.Create(request), cancellationToken);
        return body.Map(ToSettings);
    }

    public async Task<Result<SignInMethodUsers, Error>> GetAffectedAsync(string method, CancellationToken cancellationToken)
    {
        Result<UsersRepresentation, Error> body = await SendAsync<UsersRepresentation>(
            HttpMethod.Get, $"{METHODS}/{Uri.EscapeDataString(method)}/affected", null, cancellationToken);
        return body.Map(users => new SignInMethodUsers(users.Method ?? method, users.Users, users.Only));
    }

    private static ProjectSignInSettings ToSettings(SettingsRepresentation settings) =>
        new(
            (settings.Methods ?? new Dictionary<string, MethodRepresentation>()).ToDictionary(
                pair => pair.Key,
                pair => new ProjectSignInMethod(
                    pair.Value.Status ?? ProjectSignInRules.ON,
                    DateOnly.TryParseExact(pair.Value.Until, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly until) ? until : null,
                    pair.Value.Since,
                    pair.Value.Configured),
                StringComparer.Ordinal),
            string.Equals(settings.TwoFactor, ProjectSignInRules.TWO_FACTOR_REQUIRED, StringComparison.Ordinal));

    private async Task<Result<T, Error>> SendAsync<T>(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
        where T : class
    {
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

        using var request = new HttpRequestMessage(method, new Uri(realm.Value, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        HttpResponseMessage message;
        try
        {
            message = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }

        using (message)
        {
            if (!message.IsSuccessStatusCode)
            {
                return await ToErrorAsync(message, cancellationToken);
            }

            try
            {
                T? body = await message.Content.ReadFromJsonAsync<T>(cancellationToken);
                return body is null ? IdentityProviderErrors.Unavailable() : body;
            }
            catch (JsonException exception)
            {
                LogUnreadable(logger, exception);
                return IdentityProviderErrors.Unavailable();
            }
        }
    }

    private async Task<Error> ToErrorAsync(HttpResponseMessage message, CancellationToken cancellationToken)
    {
        switch (message.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                LogNotPermitted(logger);
                return ProjectSignInErrors.NotPermitted();
            case HttpStatusCode.NotFound:
                LogNotInstalled(logger);
                return ProjectSignInErrors.NotInstalled();
            case HttpStatusCode.BadRequest:
                return ProjectSignInErrors.Rejected(await ReadErrorAsync(message, cancellationToken) ?? "Способы входа не приняты.");
            default:
                LogUnexpectedResponse(logger, (int)message.StatusCode);
                return IdentityProviderErrors.Unavailable();
        }
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage message, CancellationToken cancellationToken)
    {
        try
        {
            PluginError? error = await message.Content.ReadFromJsonAsync<PluginError>(cancellationToken);
            return string.IsNullOrWhiteSpace(error?.Error) ? null : error.Error;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak is unreachable for project sign-in methods")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project sign-in methods response from Keycloak is not readable")]
    private static partial void LogUnreadable(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The service account may not read or change project sign-in methods: grant it the realm role manage-sign-in")]
    private static partial void LogNotPermitted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Keycloak has no platform-sign-in plugin: rebuild the Keycloak image")]
    private static partial void LogNotInstalled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unexpected project sign-in methods response from Keycloak: {StatusCode}")]
    private static partial void LogUnexpectedResponse(ILogger logger, int statusCode);

    private sealed record SettingsRepresentation(
        [property: JsonPropertyName("methods")] Dictionary<string, MethodRepresentation>? Methods,
        [property: JsonPropertyName("twoFactor")] string? TwoFactor);

    private sealed record MethodRepresentation(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("until")] string? Until,
        [property: JsonPropertyName("since")] DateTimeOffset? Since,
        [property: JsonPropertyName("configured")] bool? Configured);

    private sealed record SettingsRequest(
        [property: JsonPropertyName("methods")] IReadOnlyDictionary<string, MethodRequest> Methods,
        [property: JsonPropertyName("twoFactor")] string TwoFactor);

    private sealed record MethodRequest(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("until")] string? Until);

    private sealed record UsersRepresentation(
        [property: JsonPropertyName("method")] string? Method,
        [property: JsonPropertyName("users")] long Users,
        [property: JsonPropertyName("only")] long Only);

    private sealed record PluginError([property: JsonPropertyName("error")] string? Error);
}
