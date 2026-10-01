using System.Collections.Concurrent;
using PlatformAuth.Revocation;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Отметки об отзыве в памяти вместо Redis; Broken — хранилище «упало».</summary>
public sealed class InMemoryTokenRevocations : ITokenRevocationStore
{
    private readonly ConcurrentDictionary<string, long> _marks = new(StringComparer.Ordinal);

    public bool Broken { get; set; }

    public bool HasUser(Guid userId) => _marks.ContainsKey($"user:{userId}");

    public bool HasSession(string sessionId) => _marks.ContainsKey($"session:{sessionId}");

    public bool HasApp(string clientId, Guid userId) => _marks.ContainsKey($"app:{clientId}:{userId}");

    public void Reset()
    {
        _marks.Clear();
        Broken = false;
    }

    public Task<RevocationMarks> GetAsync(string subject, string? sessionId, string? authorizedParty, CancellationToken cancellationToken)
    {
        if (Broken)
        {
            throw new InvalidOperationException("Хранилище отзывов недоступно.");
        }

        return Task.FromResult(new RevocationMarks(
            _marks.TryGetValue($"user:{subject}", out long user) ? user : null,
            sessionId is not null && _marks.ContainsKey($"session:{sessionId}"),
            authorizedParty is not null && _marks.TryGetValue($"app:{authorizedParty}:{subject}", out long app) ? app : null));
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

    public Task RevokeAppAsync(string authorizedParty, string subject, DateTimeOffset at, CancellationToken cancellationToken)
    {
        _marks[$"app:{authorizedParty}:{subject}"] = at.ToUnixTimeSeconds();
        return Task.CompletedTask;
    }
}
