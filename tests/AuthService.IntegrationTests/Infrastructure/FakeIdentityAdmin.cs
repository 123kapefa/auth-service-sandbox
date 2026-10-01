using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Подставной Admin API: пользователи, которых завёл тест, и запись того, что сервис менял.</summary>
public sealed class FakeIdentityAdmin : IIdentityAdmin
{
    private readonly ConcurrentDictionary<Guid, Account> _accounts = new();

    public Collection<(Guid UserId, string Name, string Value)> AttributeChanges { get; } = [];

    /// <summary>Пользователь в «Keycloak»: роли клиента sandbox-api (без customer — он по умолчанию у всех).</summary>
    public void Add(TestUser user, IReadOnlyList<string>? clientRoles = null, bool enabled = true, bool hasPassword = true)
    {
        ArgumentNullException.ThrowIfNull(user);
        _accounts[user.Id] = new Account(
            new IdentityUser(user.Id, user.Phone.TrimStart('+'), null, user.Phone, user.ActiveMode, enabled, DateTimeOffset.UtcNow),
            clientRoles ?? user.Roles,
            hasPassword);
    }

    public void Reset()
    {
        _accounts.Clear();
        AttributeChanges.Clear();
    }

    public Task<Result<IdentityUser, Error>> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.TryGetValue(userId, out Account? account)
            ? Result.Success<IdentityUser, Error>(account.User)
            : IdentityProviderErrors.UserNotFound());

    public Task<Result<IReadOnlyList<string>, Error>> GetClientRolesAsync(Guid userId, string clientId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.TryGetValue(userId, out Account? account)
            ? Result.Success<IReadOnlyList<string>, Error>(account.ClientRoles)
            : IdentityProviderErrors.UserNotFound());

    public Task<Result<bool, Error>> HasPasswordAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.TryGetValue(userId, out Account? account)
            ? Result.Success<bool, Error>(account.HasPassword)
            : IdentityProviderErrors.UserNotFound());

    public Task<UnitResult<Error>> SetAttributeAsync(Guid userId, string name, string value, CancellationToken cancellationToken)
    {
        AttributeChanges.Add((userId, name, value));
        return Task.FromResult(UnitResult.Success<Error>());
    }

    private sealed record Account(IdentityUser User, IReadOnlyList<string> ClientRoles, bool HasPassword);
}
