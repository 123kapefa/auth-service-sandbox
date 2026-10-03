using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SharedKernel;

namespace AuthService.Infrastructure.Keycloak;

/// <summary>
/// Токен служебной учётки клиента (client_credentials) для Admin API Keycloak. Права учётки — роли realm-management
/// в realm JSON (view-users, manage-users, view-events). Токен кэшируется до истечения: Admin API зовётся часто.
/// </summary>
internal sealed partial class KeycloakServiceAccount(
    IHttpClientFactory httpClientFactory,
    IConfigurationManager<OpenIdConnectConfiguration> discovery,
    IOptions<KeycloakOptions> options,
    TimeProvider timeProvider,
    ILogger<KeycloakServiceAccount> logger) : IDisposable
{
    // Обновляем заранее, чтобы токен не истёк по дороге к Keycloak.
    private static readonly TimeSpan _refreshMargin = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    /// <summary>Внутренний адрес Admin API realm: …/admin/realms/{realm}/ — рядом с token_endpoint.</summary>
    public async Task<Result<Uri, Error>> GetAdminBaseAsync(CancellationToken cancellationToken)
    {
        Result<OpenIdConnectConfiguration, Error> configuration = await GetConfigurationAsync(cancellationToken);
        return configuration.IsSuccess ? AdminBase(configuration.Value.TokenEndpoint) : configuration.Error;
    }

    /// <summary>Внутренний адрес realm: …/realms/{realm}/ — регистрация клиентов и выдача токенов.</summary>
    public async Task<Result<Uri, Error>> GetRealmBaseAsync(CancellationToken cancellationToken)
    {
        Result<OpenIdConnectConfiguration, Error> configuration = await GetConfigurationAsync(cancellationToken);
        return configuration.IsSuccess ? RealmBase(configuration.Value.TokenEndpoint) : configuration.Error;
    }

    public async Task<Result<string, Error>> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && timeProvider.GetUtcNow() < _expiresAt)
        {
            return _token;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && timeProvider.GetUtcNow() < _expiresAt)
            {
                return _token;
            }

            return await RequestTokenAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    // …/realms/sandbox/protocol/openid-connect/token → …/realms/sandbox/
    private static Uri RealmBase(string tokenEndpoint) =>
        new(tokenEndpoint[..(tokenEndpoint.IndexOf("/protocol/", StringComparison.Ordinal) + 1)]);

    // …/realms/sandbox/protocol/openid-connect/token → …/admin/realms/sandbox/
    private static Uri AdminBase(string tokenEndpoint)
    {
        const string realms = "/realms/";
        int start = tokenEndpoint.IndexOf(realms, StringComparison.Ordinal);
        string realm = tokenEndpoint[(start + realms.Length)..].Split('/')[0];
        return new Uri($"{tokenEndpoint[..start]}/admin/realms/{realm}/");
    }

    private async Task<Result<string, Error>> RequestTokenAsync(CancellationToken cancellationToken)
    {
        Result<OpenIdConnectConfiguration, Error> configuration = await GetConfigurationAsync(cancellationToken);
        if (configuration.IsFailure)
        {
            return configuration.Error;
        }

        KeycloakOptions settings = options.Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
        });

        try
        {
            using HttpClient client = httpClientFactory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(new Uri(configuration.Value.TokenEndpoint), form, cancellationToken);
            TokenResponse? token = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                : null;
            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                LogTokenRejected(logger, (int)response.StatusCode);
                return IdentityProviderErrors.Unavailable();
            }

            _token = token.AccessToken;
            _expiresAt = timeProvider.GetUtcNow().AddSeconds(token.ExpiresIn) - _refreshMargin;
            return _token;
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(logger, exception);
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
            LogUnreachable(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak rejected the service account token request: {StatusCode}")]
    private static partial void LogTokenRejected(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak is unreachable for the service account")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
