using System.Collections.Concurrent;
using PlatformAuth.Revocation;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Отметки об отзыве и «доступ изменился» в памяти вместо Redis; Broken — хранилище «упало».</summary>
public sealed class InMemoryTokenRevocations : ITokenRevocationStore
{
    private readonly ConcurrentDictionary<string, long> _marks = new(StringComparer.Ordinal);

    public bool Broken { get; set; }

    public bool HasUser(Guid userId) => _marks.ContainsKey($"user:{userId}");

    public bool HasSession(string sessionId) => _marks.ContainsKey($"session:{sessionId}");

    public bool HasApp(string clientId, Guid userId) => _marks.ContainsKey($"app:{clientId}:{userId}");

    /// <summary>Версия доступа пользователя (умный рефреш) или null.</summary>
    public long? AccessVersion(Guid userId) => _marks.TryGetValue($"changed:user:{userId}", out long version) ? version : null;

    /// <summary>Версия роли (умный рефреш) или null.</summary>
    public long? ModeVersion(string clientId, string role) => _marks.TryGetValue($"changed:mode:{clientId}:{role}", out long version) ? version : null;

    public void Reset()
    {
        _marks.Clear();
        Broken = false;
    }

    public Task<RevocationMarks> GetAsync(TokenKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (Broken)
        {
            throw new InvalidOperationException("Хранилище отзывов недоступно.");
        }

        return Task.FromResult(new RevocationMarks(
            _marks.TryGetValue($"user:{key.Subject}", out long user) ? user : null,
            key.SessionId is not null && _marks.ContainsKey($"session:{key.SessionId}"),
            key.AuthorizedParty is not null && _marks.TryGetValue($"app:{key.AuthorizedParty}:{key.Subject}", out long app) ? app : null,
            _marks.TryGetValue($"changed:user:{key.Subject}", out long access) ? access : null,
            key.RolesClient is not null && key.ActiveMode is not null ? ModeVersion(key.RolesClient, key.ActiveMode) : null));
    }

    public Task RevokeUserAsync(string subject, DateTimeOffset at, CancellationToken cancellationToken)
    {
        _marks[$"user:{subject}"] = at.ToUnixTimeSeconds();
        return Task.CompletedTask;
    }

    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        _marks[$"session:{sessionId}"] = 1;
        return Task.CompletedTask;
    }

    public Task MarkAccessChangedAsync(string subject, long version, CancellationToken cancellationToken)
    {
        _marks[$"changed:user:{subject}"] = version;
        return Task.CompletedTask;
    }

    public Task MarkModeChangedAsync(string rolesClient, string role, long version, CancellationToken cancellationToken)
    {
        _marks[$"changed:mode:{rolesClient}:{role}"] = version;
        return Task.CompletedTask;
    }

    public Task RevokeAppAsync(string authorizedParty, string subject, DateTimeOffset at, CancellationToken cancellationToken)
    {
        _marks[$"app:{authorizedParty}:{subject}"] = at.ToUnixTimeSeconds();
        return Task.CompletedTask;
    }
}
