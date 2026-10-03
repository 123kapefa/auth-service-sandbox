using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PlatformAuth.ServiceTokens;

/// <summary>Машинный токен сервиса по его ключу доступа (client_credentials).</summary>
public interface IServiceTokenProvider
{
    /// <summary>Токен из кэша или новый, если истекает.</summary>
    Task<string> GetTokenAsync(CancellationToken cancellationToken);

    /// <summary>Забыть токен: его не приняли (ключ перевыпущен, права изменены) — следующий запрос возьмёт новый.</summary>
    void Invalidate(string token);
}

/// <summary>
/// Токен кэшируется до истечения (с запасом RefreshMargin); одновременно его запрашивает один поток.
/// Ключ отозван или неверен — исключение ServiceTokenException: без токена ходить в сервисы проекта нельзя.
/// </summary>
public sealed class ServiceTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ServiceTokenOptions> options,
    TimeProvider timeProvider) : IServiceTokenProvider, IDisposable
{
    /// <summary>Имя HttpClient для обращений к серверу входа (без ServiceTokenHandler).</summary>
    public const string HTTP_CLIENT = "PlatformAuth.ServiceTokens";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is { } cached && timeProvider.GetUtcNow() < _expiresAt)
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is { } fresh && timeProvider.GetUtcNow() < _expiresAt)
            {
                return fresh;
            }

            return await RequestAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate(string token)
    {
        if (string.Equals(_token, token, StringComparison.Ordinal))
        {
            _token = null;
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task<string> RequestAsync(CancellationToken cancellationToken)
    {
        ServiceTokenOptions settings = options.Value;
        using FormUrlEncodedContent form = new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
        });
        using HttpClient client = httpClientFactory.CreateClient(HTTP_CLIENT);
        using HttpResponseMessage response = await client.PostAsync(settings.ResolveTokenEndpoint(), form, cancellationToken);
        TokenResponse? token = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
            : null;
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new ServiceTokenException(
                $"Сервер входа не выдал токен по ключу {settings.ClientId} ({(int)response.StatusCode}): ключ отозван, неверен или сервер недоступен.");
        }

        _token = token.AccessToken;
        _expiresAt = timeProvider.GetUtcNow().AddSeconds(token.ExpiresIn) - settings.RefreshMargin;
        return _token;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>Ключ сервиса не принят сервером входа.</summary>
public sealed class ServiceTokenException : InvalidOperationException
{
    public ServiceTokenException()
    {
    }

    public ServiceTokenException(string message)
        : base(message)
    {
    }

    public ServiceTokenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
