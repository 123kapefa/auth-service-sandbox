using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SharedKernel;

namespace AuthService.Infrastructure.Keycloak;

/// <summary>
/// Вход через Keycloak по OIDC: authorization code + PKCE (S256), refresh с ротацией, завершение сессии.
/// Адреса берутся из discovery: страница входа — внешний адрес (для браузера), token — внутренний,
/// logout — рядом с token (Keycloak отдаёт end_session_endpoint внешним).
/// </summary>
internal sealed partial class KeycloakIdentityProvider(
    HttpClient httpClient,
    IConfigurationManager<OpenIdConnectConfiguration> discovery,
    IOptions<KeycloakOptions> options,
    TimeProvider timeProvider,
    ILogger<KeycloakIdentityProvider> logger) : IIdentityProvider
{
    private readonly KeycloakOptions _options = options.Value;

    public async Task<Result<AuthorizationRequest, Error>> CreateAuthorizationRequestAsync(
        Uri redirectUri,
        bool forceLogin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);

        Result<OpenIdConnectConfiguration, Error> configuration = await GetConfigurationAsync(cancellationToken);
        if (configuration.IsFailure)
        {
            return configuration.Error;
        }

        string state = RandomToken();
        string codeVerifier = RandomToken();
        var parameters = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["client_id"] = _options.ClientId,
            ["response_type"] = "code",
            ["response_mode"] = "query",
            ["scope"] = _options.Scope,
            ["redirect_uri"] = redirectUri.ToString(),
            ["state"] = state,
            ["code_challenge"] = CodeChallenge(codeVerifier),
            ["code_challenge_method"] = "S256",
        };
        if (forceLogin)
        {
            // OIDC prompt=login: Keycloak заново спрашивает вход (пароль, 2FA — если включена), auth_time обновляется.
            parameters["prompt"] = "login";
        }

        string url = QueryHelpers.AddQueryString(configuration.Value.AuthorizationEndpoint, parameters);

        return new AuthorizationRequest(new Uri(url), state, codeVerifier);
    }

    public Task<Result<TokenSet, Error>> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);

        return RequestTokensAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = redirectUri.ToString(),
            },
            cancellationToken);
    }

    public Task<Result<TokenSet, Error>> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        RequestTokensAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            },
            cancellationToken);

    public async Task<UnitResult<Error>> LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        Result<OpenIdConnectConfiguration, Error> configuration = await GetConfigurationAsync(cancellationToken);
        if (configuration.IsFailure)
        {
            return configuration.Error;
        }

        Result<HttpResponseMessage, Error> response = await PostFormAsync(
            BackchannelLogoutEndpoint(configuration.Value),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["refresh_token"] = refreshToken },
            cancellationToken);
        if (response.IsFailure)
        {
            return response.Error;
        }

        using HttpResponseMessage message = response.Value;
        return message.IsSuccessStatusCode ? UnitResult.Success<Error>() : await ToErrorAsync(message, cancellationToken);
    }

    private async Task<Result<TokenSet, Error>> RequestTokensAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        Result<OpenIdConnectConfiguration, Error> configuration = await GetConfigurationAsync(cancellationToken);
        if (configuration.IsFailure)
        {
            return configuration.Error;
        }

        Result<HttpResponseMessage, Error> response = await PostFormAsync(configuration.Value.TokenEndpoint, form, cancellationToken);
        if (response.IsFailure)
        {
            return response.Error;
        }

        using HttpResponseMessage message = response.Value;
        if (!message.IsSuccessStatusCode)
        {
            return await ToErrorAsync(message, cancellationToken);
        }

        TokenResponse? tokens = await message.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        if (tokens is null || string.IsNullOrEmpty(tokens.AccessToken) || string.IsNullOrEmpty(tokens.RefreshToken))
        {
            LogUnexpectedResponse(logger, (int)message.StatusCode);
            return IdentityProviderErrors.Unavailable();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        return new TokenSet(
            tokens.AccessToken,
            now.AddSeconds(tokens.ExpiresIn),
            tokens.RefreshToken,
            now.AddSeconds(tokens.RefreshExpiresIn));
    }

    // Клиентские учётные данные — в теле формы (client_secret_post), как принимает Keycloak.
    private async Task<Result<HttpResponseMessage, Error>> PostFormAsync(
        string endpoint,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        form["client_id"] = _options.ClientId;
        form["client_secret"] = _options.ClientSecret;

        try
        {
            using var content = new FormUrlEncodedContent(form);
            return await httpClient.PostAsync(new Uri(endpoint), content, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(logger, endpoint, exception);
            return IdentityProviderErrors.Unavailable();
        }
    }

    private async Task<Result<OpenIdConnectConfiguration, Error>> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await discovery.GetConfigurationAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            LogDiscoveryFailed(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }
    }

    // 400 invalid_grant — код/refresh-токен не принят (истёк, повторно использован, сессия закрыта).
    private async Task<Error> ToErrorAsync(HttpResponseMessage message, CancellationToken cancellationToken)
    {
        if (message.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            OAuthError? error = await message.Content.ReadFromJsonAsync<OAuthError>(cancellationToken);
            if (string.Equals(error?.Error, "invalid_grant", StringComparison.Ordinal))
            {
                return IdentityProviderErrors.GrantRejected();
            }

            LogRejected(logger, (int)message.StatusCode, error?.Error, error?.Description);
            return IdentityProviderErrors.Unavailable();
        }

        LogUnexpectedResponse(logger, (int)message.StatusCode);
        return IdentityProviderErrors.Unavailable();
    }

    // end_session_endpoint Keycloak отдаёт как адрес для браузера (внешний, localhost:8180) даже контейнеру,
    // а выход по refresh-токену — вызов сервис → Keycloak. Берём соседа token_endpoint, он внутренний:
    // .../openid-connect/token → .../openid-connect/logout.
    private static string BackchannelLogoutEndpoint(OpenIdConnectConfiguration configuration) =>
        new Uri(new Uri(configuration.TokenEndpoint), "logout").ToString();

    private static string RandomToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    private static string CodeChallenge(string codeVerifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak discovery failed")]
    private static partial void LogDiscoveryFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak endpoint {Endpoint} is unreachable")]
    private static partial void LogUnreachable(ILogger logger, string endpoint, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak rejected the request: {StatusCode} {Error} {Description}")]
    private static partial void LogRejected(ILogger logger, int statusCode, string? error, string? description);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unexpected Keycloak response: {StatusCode}")]
    private static partial void LogUnexpectedResponse(ILogger logger, int statusCode);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("refresh_expires_in")] int RefreshExpiresIn);

    private sealed record OAuthError(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? Description);
}
