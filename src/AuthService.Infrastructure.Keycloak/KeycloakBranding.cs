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
/// Оформление проекта через плагин Keycloak platform-branding ({realm}/platform-branding): читать — без токена (его же
/// читает страница входа), сохранять — токеном служебной учётки с ролью realm manage-branding. Право manage-realm
/// (менять весь realm) сервису входа для этого не нужно.
/// </summary>
internal sealed partial class KeycloakBranding(
    HttpClient http,
    KeycloakServiceAccount serviceAccount,
    ILogger<KeycloakBranding> logger) : IProjectBranding
{
    private const string PATH = "platform-branding";

    public async Task<Result<ProjectBranding, Error>> GetAsync(CancellationToken cancellationToken)
    {
        Result<Uri, Error> realm = await serviceAccount.GetRealmBaseAsync(cancellationToken);
        if (realm.IsFailure)
        {
            return realm.Error;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(realm.Value, PATH));
        Result<HttpResponseMessage, Error> response = await SendAsync(request, cancellationToken);
        if (response.IsFailure)
        {
            return response.Error;
        }

        using HttpResponseMessage message = response.Value;
        if (!message.IsSuccessStatusCode)
        {
            return await ToErrorAsync(message, cancellationToken);
        }

        try
        {
            BrandingRepresentation? body = await message.Content.ReadFromJsonAsync<BrandingRepresentation>(cancellationToken);
            return body is null
                ? IdentityProviderErrors.Unavailable()
                : new ProjectBranding(body.Name ?? string.Empty, body.Logo, body.Color, body.Theme ?? BrandingRules.THEME_SYSTEM);
        }
        catch (JsonException exception)
        {
            LogUnreadable(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }
    }

    public async Task<UnitResult<Error>> SetAsync(ProjectBranding branding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(branding);
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

        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(realm.Value, PATH))
        {
            Content = JsonContent.Create(new BrandingRepresentation(branding.Name, branding.Logo, branding.Color, branding.Theme)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        Result<HttpResponseMessage, Error> response = await SendAsync(request, cancellationToken);
        if (response.IsFailure)
        {
            return response.Error;
        }

        using HttpResponseMessage message = response.Value;
        return message.IsSuccessStatusCode ? UnitResult.Success<Error>() : await ToErrorAsync(message, cancellationToken);
    }

    private async Task<Result<HttpResponseMessage, Error>> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }
    }

    private async Task<Error> ToErrorAsync(HttpResponseMessage message, CancellationToken cancellationToken)
    {
        switch (message.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                LogNotPermitted(logger);
                return BrandingErrors.NotPermitted();
            case HttpStatusCode.NotFound:
                LogNotInstalled(logger);
                return BrandingErrors.NotInstalled();
            case HttpStatusCode.BadRequest:
                return BrandingErrors.Rejected(await ReadErrorAsync(message, cancellationToken) ?? "Оформление не принято.");
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
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak is unreachable for project branding")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project branding response from Keycloak is not readable")]
    private static partial void LogUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The service account may not change project branding: grant it the realm role manage-branding")]
    private static partial void LogNotPermitted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Keycloak has no platform-branding plugin: rebuild the Keycloak image")]
    private static partial void LogNotInstalled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unexpected project branding response from Keycloak: {StatusCode}")]
    private static partial void LogUnexpectedResponse(ILogger logger, int statusCode);

    // Без своего логотипа и цвета плагин их не присылает; отправляем null явно — «убрать».
    private sealed record BrandingRepresentation(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("logo")] string? Logo,
        [property: JsonPropertyName("color")] string? Color,
        [property: JsonPropertyName("theme")] string? Theme);

    private sealed record PluginError([property: JsonPropertyName("error")] string? Error);
}
