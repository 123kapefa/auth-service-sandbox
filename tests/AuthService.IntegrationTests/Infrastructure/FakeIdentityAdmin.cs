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
    /// <summary>Приложения проекта: клиенты с ролью access-denied.</summary>
    public static readonly string[] Apps = ["account-console", "sandbox-bff"];

    private readonly ConcurrentDictionary<Guid, Account> _accounts = new();

    /// <summary>Состав ролей: «clientId/роль» → имена ролей (прав) в составе.</summary>
    private readonly ConcurrentDictionary<string, HashSet<string>> _composites = new(StringComparer.Ordinal);

    /// <summary>Роли клиентов приложений: clientId → имя → роль.</summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, AppRole>> _appRoles = new(StringComparer.Ordinal);

    /// <summary>Записанные атрибуты (удаления — со значением null).</summary>
    public Collection<(Guid UserId, string Name, string? Value)> AttributeChanges { get; } = [];

    /// <summary>Выданные и снятые роли клиента: (пользователь, выдать, снять).</summary>
    public Collection<(Guid UserId, string[] Grant, string[] Revoke)> RoleChanges { get; } = [];

    /// <summary>Сессии пользователей: id сессии → (пользователь, приложения).</summary>
    private readonly ConcurrentDictionary<string, (Guid UserId, string[] Apps)> _sessions = new(StringComparer.Ordinal);

    /// <summary>События входа «Keycloak», новые сверху.</summary>
    public Collection<LoginEvent> LoginEvents { get; } = [];

    /// <summary>Какие сессии сервис завершил по одной.</summary>
    public Collection<string> DeletedSessions { get; } = [];

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
            hasPassword,
            []);
    }

    public void Reset()
    {
        _accounts.Clear();
        SeedAppRoles();
        AttributeChanges.Clear();
        Logouts.Clear();
        DeletedSessions.Clear();
        LoginEvents.Clear();
        _sessions.Clear();
        RoleChanges.Clear();
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

    /// <summary>Как в realm: у sandbox-api встроенные роли; права заводит сверка каталога; у клиентов входа — access-denied.</summary>
    public void SeedAppRoles()
    {
        _appRoles.Clear();
        _composites.Clear();
        _appRoles["sandbox-api"] = new(StringComparer.Ordinal);
        _composites["sandbox-api/customer"] = ["profile.view"];
        _composites["sandbox-api/manager"] = ["profile.view", "users.view", "users.block"];
        _composites["sandbox-api/admin"] =
            ["profile.view", "users.view", "users.block", "users.roles.manage", "users.sessions.manage", "audit.view", "roles.manage"];
        foreach (string role in new[] { "customer", "manager", "admin" })
        {
            _appRoles["sandbox-api"][role] = new AppRole(Guid.NewGuid(), role, role, AppRoleKinds.ROLE, null, true);
        }

        foreach (string app in Apps)
        {
            _appRoles[app] = new(StringComparer.Ordinal) { [AppRoles.ACCESS_DENIED] = new AppRole(Guid.NewGuid(), AppRoles.ACCESS_DENIED, null, AppRoleKinds.ROLE, null, false) };
        }
    }

    public IReadOnlyCollection<AppRole> AppRolesOf(string clientId) => [.. _appRoles[clientId].Values];

    public Task<Result<IReadOnlyList<AppRole>, Error>> GetAppRolesAsync(string clientId, CancellationToken cancellationToken) =>
        Task.FromResult(_appRoles.TryGetValue(clientId, out ConcurrentDictionary<string, AppRole>? roles)
            ? Result.Success<IReadOnlyList<AppRole>, Error>([.. roles.Values.OrderBy(role => role.Name, StringComparer.Ordinal)])
            : IdentityProviderErrors.AppNotFound());

    public Task<UnitResult<Error>> CreateAppRoleAsync(string clientId, AppRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (!_appRoles.TryGetValue(clientId, out ConcurrentDictionary<string, AppRole>? roles))
        {
            return Task.FromResult(UnitResult.Failure(IdentityProviderErrors.AppNotFound()));
        }

        return Task.FromResult(roles.TryAdd(role.Name, role with { Id = Guid.NewGuid() })
            ? UnitResult.Success<Error>()
            : UnitResult.Failure(IdentityProviderErrors.RoleExists(role.Name)));
    }

    public Task<UnitResult<Error>> UpdateAppRoleAsync(string clientId, AppRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        ConcurrentDictionary<string, AppRole> roles = _appRoles[clientId];
        roles[role.Name] = role with { Id = roles.TryGetValue(role.Name, out AppRole? old) ? old.Id : Guid.NewGuid() };
        return Task.FromResult(UnitResult.Success<Error>());
    }

    public IReadOnlySet<string> CompositesOf(string clientId, string role) =>
        _composites.TryGetValue($"{clientId}/{role}", out HashSet<string>? names) ? names : new HashSet<string>(StringComparer.Ordinal);

    public Task<Result<IReadOnlyList<string>, Error>> GetRoleCompositesAsync(string clientId, string roleName, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<string>, Error>([.. CompositesOf(clientId, roleName).Order(StringComparer.Ordinal)]));

    public Task<UnitResult<Error>> SetRoleCompositesAsync(
        string clientId,
        string roleName,
        IReadOnlyCollection<string> add,
        IReadOnlyCollection<string> remove,
        CancellationToken cancellationToken)
    {
        HashSet<string> names = _composites.GetOrAdd($"{clientId}/{roleName}", _ => new HashSet<string>(StringComparer.Ordinal));
        names.UnionWith(add);
        names.ExceptWith(remove);
        return Task.FromResult(UnitResult.Success<Error>());
    }

    public Task<UnitResult<Error>> DeleteAppRoleAsync(string clientId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(_appRoles[clientId].TryRemove(name, out _)
            ? UnitResult.Success<Error>()
            : UnitResult.Failure(IdentityProviderErrors.RoleNotFound(name)));

    public Task<UnitResult<Error>> SetClientRolesAsync(
        Guid userId,
        string clientId,
        IReadOnlyCollection<string> grant,
        IReadOnlyCollection<string> revoke,
        CancellationToken cancellationToken)
    {
        if (!_accounts.TryGetValue(userId, out Account? account))
        {
            return Task.FromResult(UnitResult.Failure(IdentityProviderErrors.UserNotFound()));
        }

        RoleChanges.Add((userId, [.. grant], [.. revoke]));
        _accounts[userId] = Apps.Contains(clientId)
            ? account with { DeniedApps = grant.Count > 0 ? [.. account.DeniedApps.Append(clientId)] : [.. account.DeniedApps.Where(app => app != clientId)] }
            : account with { ClientRoles = [.. account.ClientRoles.Except(revoke).Union(grant)] };
        return Task.FromResult(UnitResult.Success<Error>());
    }

    public Task<Result<IReadOnlyList<AppAccessInfo>, Error>> GetAppAccessAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.TryGetValue(userId, out Account? account)
            ? Result.Success<IReadOnlyList<AppAccessInfo>, Error>(
                [.. Apps.Select(app => new AppAccessInfo(app, $"Приложение {app}", !account.DeniedApps.Contains(app)))])
            : IdentityProviderErrors.UserNotFound());

    public Task<Result<IReadOnlyList<LoginEvent>, Error>> GetLoginEventsAsync(
        Guid? userId,
        IReadOnlyCollection<string> types,
        int first,
        int max,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<LoginEvent>, Error>(
        [
            .. LoginEvents
                .Where(found => (userId is null || found.UserId == userId) && types.Contains(found.Type))
                .Skip(first)
                .Take(max),
        ]));

    public void AddSession(TestUser user, string sessionId, params string[] apps)
    {
        ArgumentNullException.ThrowIfNull(user);
        _sessions[sessionId] = (user.Id, apps);
    }

    public Task<Result<IReadOnlyList<IdentitySession>, Error>> GetSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<IdentitySession>, Error>(
        [
            .. _sessions
                .Where(session => session.Value.UserId == userId)
                .OrderBy(session => session.Key, StringComparer.Ordinal)
                .Select(session => new IdentitySession(session.Key, "10.0.0.1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, session.Value.Apps)),
        ]));

    public Task<UnitResult<Error>> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        DeletedSessions.Add(sessionId);
        _sessions.TryRemove(sessionId, out _);
        return Task.FromResult(UnitResult.Success<Error>());
    }

    public Task<UnitResult<Error>> LogoutUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (string sessionId in _sessions.Where(session => session.Value.UserId == userId).Select(session => session.Key).ToList())
        {
            _sessions.TryRemove(sessionId, out _);
        }

        Logouts.Add(userId);
        return Task.FromResult(UnitResult.Success<Error>());
    }

    private sealed record Account(IdentityUser User, IReadOnlyList<string> ClientRoles, bool HasPassword, IReadOnlyList<string> DeniedApps);
}
