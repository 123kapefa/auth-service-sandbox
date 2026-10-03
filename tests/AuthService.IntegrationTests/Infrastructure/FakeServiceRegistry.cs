using System.Collections.Concurrent;
using AuthService.Core.Abstractions;
using AuthService.Domain.ServiceKeys;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>
/// Регистрация клиентов Keycloak в памяти: секрет и токен управления — новые на каждое обращение, старый токен
/// управления после использования не принимается (как в Keycloak). Configured=false — нет разрешения на регистрацию.
/// </summary>
public sealed class FakeServiceRegistry : IServiceRegistry
{
    private readonly ConcurrentDictionary<string, Client> _clients = new(StringComparer.Ordinal);
    private int _counter;

    public bool Configured { get; set; } = true;

    public bool IsConfigured => Configured;

    public IReadOnlyCollection<string> ClientIds => [.. _clients.Keys];

    public string? SecretOf(string clientId) => _clients.TryGetValue(clientId, out Client? client) ? client.Secret : null;

    public Guid ServiceAccountOf(string clientId) => _clients[clientId].ServiceAccountId;

    public void Reset()
    {
        _clients.Clear();
        Configured = true;
    }

    public Task<Result<RegisteredService, Error>> RegisterAsync(string clientId, string name, CancellationToken cancellationToken)
    {
        if (_clients.ContainsKey(clientId))
        {
            return Task.FromResult(Result.Failure<RegisteredService, Error>(ServiceRegistryErrors.ClientExists(clientId)));
        }

        Client client = new(Next("secret"), Next("rat"), Guid.NewGuid());
        _clients[clientId] = client;
        return Task.FromResult(Result.Success<RegisteredService, Error>(new RegisteredService(client.Secret, client.RegistrationToken, client.ServiceAccountId)));
    }

    public Task<Result<RegisteredService, Error>> RotateSecretAsync(string clientId, string registrationToken, CancellationToken cancellationToken)
    {
        if (!_clients.TryGetValue(clientId, out Client? client) || client.RegistrationToken != registrationToken)
        {
            return Task.FromResult(Result.Failure<RegisteredService, Error>(ServiceRegistryErrors.RegistrationRejected()));
        }

        Client rotated = client with { Secret = Next("secret"), RegistrationToken = Next("rat") };
        _clients[clientId] = rotated;
        return Task.FromResult(Result.Success<RegisteredService, Error>(new RegisteredService(rotated.Secret, rotated.RegistrationToken, rotated.ServiceAccountId)));
    }

    public Task<UnitResult<Error>> DeleteAsync(string clientId, string registrationToken, CancellationToken cancellationToken)
    {
        if (!_clients.TryGetValue(clientId, out Client? client) || client.RegistrationToken != registrationToken)
        {
            return Task.FromResult(UnitResult.Failure<Error>(ServiceRegistryErrors.RegistrationRejected()));
        }

        _clients.TryRemove(clientId, out _);
        return Task.FromResult(UnitResult.Success<Error>());
    }

    private string Next(string prefix) => $"{prefix}-{Interlocked.Increment(ref _counter)}";

    private sealed record Client(string Secret, string RegistrationToken, Guid ServiceAccountId);
}

/// <summary>Ключи сервисов в памяти вместо Postgres.</summary>
public sealed class InMemoryServiceKeyStore : IServiceKeyStore
{
    private readonly ConcurrentDictionary<string, ServiceKey> _keys = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ServiceKey> Keys => [.. _keys.Values];

    public void Reset() => _keys.Clear();

    public Task<IReadOnlyList<ServiceKey>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ServiceKey>>([.. _keys.Values.OrderBy(key => key.Code, StringComparer.Ordinal)]);

    public Task<ServiceKey?> FindAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(_keys.GetValueOrDefault(code));

    public Task AddAsync(ServiceKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        _keys[key.Code] = key;
        return Task.CompletedTask;
    }

    public Task SaveAsync(ServiceKey key, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveAsync(ServiceKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        _keys.TryRemove(key.Code, out _);
        return Task.CompletedTask;
    }
}
