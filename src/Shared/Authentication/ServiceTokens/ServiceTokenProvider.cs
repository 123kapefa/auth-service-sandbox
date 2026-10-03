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

    // Токен и срок — одной неизменяемой парой: читается без блокировки, меняется целиком.
    private volatile CachedToken? _cached;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_cached is { } cached && timeProvider.GetUtcNow() < cached.RefreshAt)
        {
            return cached.Token;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is { } fresh && timeProvider.GetUtcNow() < fresh.RefreshAt)
            {
                return fresh.Token;
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
        CachedToken? cached = _cached;
        if (cached is not null && string.Equals(cached.Token, token, StringComparison.Ordinal))
        {
            Interlocked.CompareExchange(ref _cached, null, cached);
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

        // Запас до истечения — не больше половины срока: иначе короткий токен запрашивался бы на каждый вызов.
        TimeSpan lifetime = TimeSpan.FromSeconds(token.ExpiresIn);
        TimeSpan margin = settings.RefreshMargin < lifetime / 2 ? settings.RefreshMargin : lifetime / 2;
        _cached = new CachedToken(token.AccessToken, timeProvider.GetUtcNow() + lifetime - margin);
        return token.AccessToken;
    }

    private sealed record CachedToken(string Token, DateTimeOffset RefreshAt);

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
