using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Подставной Admin API: пользователи, которых завёл тест, и запись того, что сервис менял.</summary>
public sealed class FakeIdentityAdmin : IIdentityAdmin
{
    private readonly ConcurrentDictionary<Guid, Account> _accounts = new();

    /// <summary>Записанные атрибуты (удаления — со значением null).</summary>
    public Collection<(Guid UserId, string Name, string? Value)> AttributeChanges { get; } = [];

    /// <summary>У кого сервис завершил все сессии.</summary>
    public Collection<Guid> Logouts { get; } = [];

    /// <summary>С какими параметрами сервис искал пользователей.</summary>
    public Collection<(string? Search, int First, int Max)> Searches { get; } = [];

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
        Logouts.Clear();
        Searches.Clear();
    }

    public Task<Result<IdentityUser, Error>> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.TryGetValue(userId, out Account? account)
            ? Result.Success<IdentityUser, Error>(account.User)
            : IdentityProviderErrors.UserNotFound());

    // Как search Admin API Keycloak: подстрока в логине или почте, по логину по возрастанию.
    public Task<Result<IReadOnlyList<IdentityUser>, Error>> SearchUsersAsync(string? search, int first, int max, CancellationToken cancellationToken)
    {
        Searches.Add((search, first, max));
        IReadOnlyList<IdentityUser> users =
        [
            .. _accounts.Values
                .Select(account => account.User)
                .Where(user => search is null
                    || user.Username.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || (user.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                .OrderBy(user => user.Username, StringComparer.Ordinal)
                .Skip(first)
                .Take(max),
        ];
        return Task.FromResult(Result.Success<IReadOnlyList<IdentityUser>, Error>(users));
    }

    public Task<Result<IReadOnlyList<string>, Error>> GetClientRolesAsync(Guid userId, string clientId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.TryGetValue(userId, out Account? account)
            ? Result.Success<IReadOnlyList<string>, Error>(account.ClientRoles)
            : IdentityProviderErrors.UserNotFound());

    public Task<Result<bool, Error>> HasPasswordAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.TryGetValue(userId, out Account? account)
            ? Result.Success<bool, Error>(account.HasPassword)
            : IdentityProviderErrors.UserNotFound());

    public Task<UnitResult<Error>> UpdateUserAsync(Guid userId, UserChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!_accounts.TryGetValue(userId, out Account? account))
        {
            return Task.FromResult(UnitResult.Failure(IdentityProviderErrors.UserNotFound()));
        }

        IdentityUser user = account.User with { Enabled = changes.Enabled ?? account.User.Enabled };
        foreach ((string name, string? value) in changes.Attributes)
        {
            AttributeChanges.Add((userId, name, value));
            user = name switch
            {
                UserAttributes.RESTRICTION_REASON => user with { RestrictionReason = value },
                UserAttributes.RESTRICTED_AT => user with { RestrictedAt = value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture) },
                "active_mode" => user with { ActiveMode = value },
                _ => user,
            };
        }

        _accounts[userId] = account with { User = user };
        return Task.FromResult(UnitResult.Success<Error>());
    }

    public Task<UnitResult<Error>> LogoutUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        Logouts.Add(userId);
        return Task.FromResult(UnitResult.Success<Error>());
    }

    private sealed record Account(IdentityUser User, IReadOnlyList<string> ClientRoles, bool HasPassword);
}
