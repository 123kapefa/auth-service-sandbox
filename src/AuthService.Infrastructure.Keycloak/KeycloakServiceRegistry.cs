using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using SharedKernel;

namespace AuthService.Infrastructure.Keycloak;

/// <summary>
/// Ключи сервисов через client registration API Keycloak ({realm}/clients-registrations/default). Регистрация — по
/// разрешению техника (initial access token, Keycloak:RegistrationToken), перевыпуск и удаление — токеном управления
/// клиентом из ответа регистрации. Права «управлять всеми клиентами» сервису входа не нужны; политики регистрации
/// Keycloak не дают зарегистрированному клиенту выдать себе лишнее (свои мапперы, чужие области).
/// </summary>
internal sealed partial class KeycloakServiceRegistry(
    HttpClient http,
    KeycloakServiceAccount serviceAccount,
    IOptions<KeycloakOptions> options,
    ILogger<KeycloakServiceRegistry> logger) : IServiceRegistry
{
    private const string REGISTRATIONS = "clients-registrations/default";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.RegistrationToken);

    public async Task<Result<RegisteredService, Error>> RegisterAsync(string clientId, string name, CancellationToken cancellationToken)
    {
        Result<Uri, Error> realm = await serviceAccount.GetRealmBaseAsync(cancellationToken);
        if (realm.IsFailure)
        {
            return realm.Error;
        }

        // Только служебная учётка: без входа пользователей и без пароля пользователя — сервис получает токены своим ключом.
        ClientRepresentation client = new(clientId, name, PublicClient: false, ServiceAccountsEnabled: true,
            StandardFlowEnabled: false, DirectAccessGrantsEnabled: false, ImplicitFlowEnabled: false);
        using HttpRequestMessage request = Request(HttpMethod.Post, new Uri(realm.Value, REGISTRATIONS), options.Value.RegistrationToken);
        request.Content = JsonContent.Create(client);

        Result<RegisteredClient, Error> registered = await SendAsync(request, clientId, cancellationToken);
        return registered.IsSuccess ? await WithServiceAccountAsync(realm.Value, clientId, registered.Value, cancellationToken) : registered.Error;
    }

    public async Task<Result<RegisteredService, Error>> RotateSecretAsync(string clientId, string registrationToken, CancellationToken cancellationToken)
    {
        Result<Uri, Error> realm = await serviceAccount.GetRealmBaseAsync(cancellationToken);
        if (realm.IsFailure)
        {
            return realm.Error;
        }

        Uri address = new(realm.Value, $"{REGISTRATIONS}/{Uri.EscapeDataString(clientId)}");
        using HttpRequestMessage read = Request(HttpMethod.Get, address, registrationToken);
        Result<RegisteredClient, Error> current = await SendAsync(read, clientId, cancellationToken);
        if (current.IsFailure)
        {
            return current.Error;
        }

        // Новый секрет задаёт сервис входа: Keycloak сразу перестаёт принимать старый.
        using HttpRequestMessage update = Request(HttpMethod.Put, address, current.Value.RegistrationAccessToken);
        update.Content = JsonContent.Create(current.Value.Representation with { Secret = NewSecret() });
        Result<RegisteredClient, Error> rotated = await SendAsync(update, clientId, cancellationToken);
        return rotated.IsSuccess ? await WithServiceAccountAsync(realm.Value, clientId, rotated.Value, cancellationToken) : rotated.Error;
    }

    public async Task<UnitResult<Error>> DeleteAsync(string clientId, string registrationToken, CancellationToken cancellationToken)
    {
        Result<Uri, Error> realm = await serviceAccount.GetRealmBaseAsync(cancellationToken);
        if (realm.IsFailure)
        {
            return realm.Error;
        }

        using HttpRequestMessage request = Request(HttpMethod.Delete, new Uri(realm.Value, $"{REGISTRATIONS}/{Uri.EscapeDataString(clientId)}"), registrationToken);
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            // Уже удалён (например, вручную в консоли) — ключа всё равно нет: считаем отозванным.
            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound
                ? UnitResult.Success<Error>()
                : Failure(response.StatusCode, clientId);
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }
    }

    // Учётка сервиса (sub его токенов) — из первого токена по новому ключу: так её узнаём без права читать пользователей.
    private async Task<Result<RegisteredService, Error>> WithServiceAccountAsync(
        Uri realm, string clientId, RegisteredClient client, CancellationToken cancellationToken)
    {
        string secret = client.Representation.Secret ?? string.Empty;
        using FormUrlEncodedContent form = new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        });
        try
        {
            using HttpResponseMessage response = await http.PostAsync(new Uri(realm, "protocol/openid-connect/token"), form, cancellationToken);
            TokenResponse? token = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken) : null;
            if (token is null || !Guid.TryParse(new JsonWebToken(token.AccessToken).Subject, out Guid serviceAccountId))
            {
                LogNoServiceAccount(logger, clientId, (int)response.StatusCode);
                return IdentityProviderErrors.Unavailable();
            }

            return new RegisteredService(secret, client.RegistrationAccessToken, serviceAccountId);
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }
    }

    private async Task<Result<RegisteredClient, Error>> SendAsync(HttpRequestMessage request, string clientId, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(response.StatusCode, clientId);
            }

            JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            ClientRepresentation representation = body.Deserialize<ClientRepresentation>()!;
            string token = body.TryGetProperty("registrationAccessToken", out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;
            return new RegisteredClient(representation, token);
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }
    }

    private Error Failure(HttpStatusCode status, string clientId)
    {
        LogRejected(logger, clientId, (int)status);
        return status switch
        {
            HttpStatusCode.Unauthorized => ServiceRegistryErrors.RegistrationRejected(),
            HttpStatusCode.Conflict or HttpStatusCode.BadRequest => ServiceRegistryErrors.ClientExists(clientId),
            _ => IdentityProviderErrors.Unavailable(),
        };
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri address, string bearer)
    {
        HttpRequestMessage request = new(method, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return request;
    }

    // 32 случайных байта — как секрет, который генерирует сам Keycloak.
    private static string NewSecret() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak rejected client registration request for {ClientId}: {StatusCode}")]
    private static partial void LogRejected(ILogger logger, string clientId, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No service account token for registered client {ClientId}: {StatusCode}")]
    private static partial void LogNoServiceAccount(ILogger logger, string clientId, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak is unreachable for client registration")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);

    private sealed record RegisteredClient(ClientRepresentation Representation, string RegistrationAccessToken);

    // Только поля, которые сервис входа задаёт и читает; остальное Keycloak оставляет по умолчанию.
    private sealed record ClientRepresentation(
        [property: JsonPropertyName("clientId")] string ClientId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("publicClient")] bool PublicClient,
        [property: JsonPropertyName("serviceAccountsEnabled")] bool ServiceAccountsEnabled,
        [property: JsonPropertyName("standardFlowEnabled")] bool StandardFlowEnabled,
        [property: JsonPropertyName("directAccessGrantsEnabled")] bool DirectAccessGrantsEnabled,
        [property: JsonPropertyName("implicitFlowEnabled")] bool ImplicitFlowEnabled,
        [property: JsonPropertyName("secret"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Secret = null);

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
