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
    /// <summary>Приложение сервиса (audience): роли пользователя в нём — ClientRoles.</summary>
    public const string SERVICE_APP = "sandbox-api";

    /// <summary>Второе демо-приложение со своими правами и ролями.</summary>
    public const string MANAGER_1C = "manager-1c";

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

    /// <summary>У кого включена 2FA (OTP и коды восстановления).</summary>
    private readonly ConcurrentDictionary<Guid, bool> _twoFactor = new();

    /// <summary>Подробные учётные данные для кабинета (кроме пароля — он от hasPassword).</summary>
    private readonly ConcurrentDictionary<Guid, List<UserCredential>> _credentials = new();

    /// <summary>Письма Keycloak с действиями: (пользователь, действия, срок ссылки).</summary>
    public Collection<(Guid UserId, string[] Actions, TimeSpan Lifespan)> ActionEmails { get; } = [];

    /// <summary>Какие сессии сервис завершил по одной.</summary>
    public Collection<string> DeletedSessions { get; } = [];

    /// <summary>У кого сервис завершил все сессии.</summary>
    public Collection<Guid> Logouts { get; } = [];

    /// <summary>С какими параметрами сервис искал пользователей.</summary>
    public Collection<(string? Search, int First, int Max)> Searches { get; } = [];

    /// <summary>Пользователь в «Keycloak»: роли клиента sandbox-api (без customer — он по умолчанию у всех).</summary>
    public void Add(
        TestUser user,
        IReadOnlyList<string>? clientRoles = null,
        bool enabled = true,
        bool hasPassword = true,
        string? email = null,
        bool emailVerified = false)
    {
        ArgumentNullException.ThrowIfNull(user);
        _accounts[user.Id] = new Account(
            new IdentityUser(user.Id, user.Phone.TrimStart('+'), email, user.Phone, user.ActiveMode, enabled, DateTimeOffset.UtcNow,
                EmailVerified: emailVerified),
            clientRoles ?? user.Roles,
            hasPassword,
            []);
    }

    /// <summary>Учётка сервиса проекта (клиент с ключом доступа) — как service-account-… в Keycloak.</summary>
    public void AddServiceAccount(Guid id, string clientId) =>
        _accounts[id] = new Account(
            new IdentityUser(id, "service-account-" + clientId, null, null, null, true, DateTimeOffset.UtcNow),
            [],
            false,
            []);

    /// <summary>Роли пользователя в другом приложении (например, «Менеджер 1С»).</summary>
    public void SetAppRoles(TestUser user, string clientId, params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        _accounts[user.Id] = _accounts[user.Id].WithRolesIn(clientId, roles);
    }

    /// <summary>Включить пользователю 2FA (как приложение-аутентификатор и коды восстановления).</summary>
    public void EnableTwoFactor(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        _twoFactor[user.Id] = true;
    }

    /// <summary>Учётные данные пользователя для кабинета: приложение-аутентификатор, ключ доступа, коды.</summary>
    public void AddCredential(TestUser user, UserCredential credential)
    {
        ArgumentNullException.ThrowIfNull(user);
        _credentials.GetOrAdd(user.Id, _ => []).Add(credential);
    }

    /// <summary>Почта пользователя.</summary>
    public void SetEmail(TestUser user, string email)
    {
        ArgumentNullException.ThrowIfNull(user);
        _accounts[user.Id] = _accounts[user.Id] with { User = _accounts[user.Id].User with { Email = email } };
    }

    public void Reset()
    {
        _accounts.Clear();
        _twoFactor.Clear();
        _credentials.Clear();
        ActionEmails.Clear();
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

    public Task<Result<IdentityUser?, Error>> FindUserExactAsync(string login, CancellationToken cancellationToken)
    {
        IdentityUser? user = _accounts.Values
            .Select(account => account.User)
            .FirstOrDefault(found => string.Equals(found.Username, login, StringComparison.OrdinalIgnoreCase)
                || string.Equals(found.Email, login, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(Result.Success<IdentityUser?, Error>(user));
    }

    public Task<Result<IReadOnlyList<IdentityUser>, Error>> GetRoleUsersAsync(
        string clientId,
        string roleName,
        int first,
        int max,
        CancellationToken cancellationToken)
    {
        if (!_appRoles.ContainsKey(clientId))
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<IdentityUser>, Error>(IdentityProviderErrors.AppNotFound()));
        }

        IReadOnlyList<IdentityUser> users =
        [
            .. _accounts.Values
                .Where(account => account.RolesIn(clientId).Contains(roleName, StringComparer.Ordinal))
                .Select(account => account.User)
                .OrderBy(user => user.Username, StringComparer.Ordinal)
                .Skip(first)
                .Take(max),
        ];
        return Task.FromResult(Result.Success<IReadOnlyList<IdentityUser>, Error>(users));
    }

    // Как FGAP v2: учётка видит клиенты приложений и клиенты входа.
    public Task<Result<IReadOnlyList<ClientInfo>, Error>> GetClientsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<ClientInfo>, Error>(
            [.. _appRoles.Keys.Order(StringComparer.Ordinal).Select(clientId => new ClientInfo(clientId, $"Приложение {clientId}"))]));

    public Task<Result<IReadOnlyList<string>, Error>> GetClientRolesAsync(Guid userId, string clientId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.TryGetValue(userId, out Account? account)
            ? Result.Success<IReadOnlyList<string>, Error>(
                account.DeniedApps.Contains(clientId) ? [.. account.RolesIn(clientId), AppRoles.ACCESS_DENIED] : account.RolesIn(clientId))
            : IdentityProviderErrors.UserNotFound());

    // Как в realm: customer — роль по умолчанию, плюс права в составе ролей.
    public Task<Result<IReadOnlyList<string>, Error>> GetEffectiveClientRolesAsync(Guid userId, string clientId, CancellationToken cancellationToken)
    {
        if (!_appRoles.ContainsKey(clientId))
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<string>, Error>(IdentityProviderErrors.AppNotFound()));
        }

        if (!_accounts.TryGetValue(userId, out Account? account))
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<string>, Error>(IdentityProviderErrors.UserNotFound()));
        }

        string[] roles = [.. DefaultRolesOf(clientId).Union(account.RolesIn(clientId), StringComparer.Ordinal)];
        IReadOnlyList<string> effective = [.. roles.Union(roles.SelectMany(role => CompositesOf(clientId, role)), StringComparer.Ordinal)];
        return Task.FromResult(Result.Success<IReadOnlyList<string>, Error>(effective));
    }

    /// <summary>Роли по умолчанию приложения: у sandbox-api — customer.</summary>
    private static string[] DefaultRolesOf(string clientId) =>
        string.Equals(clientId, SERVICE_APP, StringComparison.Ordinal) ? ["customer"] : [];

    public Task<Result<IReadOnlyList<string>, Error>> GetCredentialTypesAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_accounts.TryGetValue(userId, out Account? account))
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<string>, Error>(IdentityProviderErrors.UserNotFound()));
        }

        List<string> types = account.HasPassword ? [Credentials.PASSWORD] : [];
        if (_twoFactor.ContainsKey(userId))
        {
            types.AddRange(Credentials.TwoFactor);
        }

        return Task.FromResult(Result.Success<IReadOnlyList<string>, Error>(types));
    }

    public Task<Result<IReadOnlyList<UserCredential>, Error>> GetCredentialsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_accounts.TryGetValue(userId, out Account? account))
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<UserCredential>, Error>(IdentityProviderErrors.UserNotFound()));
        }

        List<UserCredential> credentials = account.HasPassword
            ? [new UserCredential("password-" + userId, Credentials.PASSWORD, null, DateTimeOffset.UnixEpoch)]
            : [];
        credentials.AddRange(_credentials.TryGetValue(userId, out List<UserCredential>? added) ? added : []);
        return Task.FromResult(Result.Success<IReadOnlyList<UserCredential>, Error>(credentials));
    }

    public Task<Result<int, Error>> RemoveCredentialsAsync(Guid userId, IReadOnlyCollection<string> types, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(types);
        int removed = types.Contains(Credentials.OTP) && _twoFactor.TryRemove(userId, out _) ? Credentials.TwoFactor.Count : 0;
        return Task.FromResult(Result.Success<int, Error>(removed));
    }

    public Task<UnitResult<Error>> SendActionsEmailAsync(
        Guid userId,
        IReadOnlyCollection<string> actions,
        TimeSpan lifespan,
        CancellationToken cancellationToken)
    {
        ActionEmails.Add((userId, [.. actions], lifespan));
        return Task.FromResult(UnitResult.Success<Error>());
    }

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
                _ when name.StartsWith(UserAttributes.APP_MODE_PREFIX, StringComparison.Ordinal) => user with
                {
                    AppModes = WithMode(user.AppModes, name[UserAttributes.APP_MODE_PREFIX.Length..], value),
                },
                _ => user,
            };
        }

        _accounts[userId] = account with { User = user };
        return Task.FromResult(UnitResult.Success<Error>());
    }

    private static Dictionary<string, string> WithMode(IReadOnlyDictionary<string, string>? modes, string clientId, string? mode)
    {
        Dictionary<string, string> result = new(modes ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        if (mode is null)
        {
            result.Remove(clientId);
        }
        else
        {
            result[clientId] = mode;
        }

        return result;
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
            ["profile.view", "users.view", "users.block", "users.roles.manage", "users.sessions.manage", "users.credentials.manage", "audit.view", "roles.manage"];
        foreach (string role in new[] { "customer", "manager", "admin" })
        {
            _appRoles["sandbox-api"][role] = new AppRole(Guid.NewGuid(), role, role, AppRoleKinds.ROLE, null, true);
        }

        // «Менеджер 1С»: свои права и роли, admin — с управлением ролями приложения.
        _appRoles[MANAGER_1C] = new(StringComparer.Ordinal);
        foreach (string permission in new[] { "orders.view", "orders.edit", "reports.view", "users.roles.manage", "roles.manage", "users.block", "audit.view" })
        {
            _appRoles[MANAGER_1C][permission] = new AppRole(Guid.NewGuid(), permission, permission, AppRoleKinds.PERMISSION, AppRoleKinds.SOURCE_CODE, false);
        }

        foreach ((string role, string[] permissions) in new[]
        {
            ("operator", new[] { "orders.view", "orders.edit" }),
            ("accountant", new[] { "orders.view", "reports.view" }),
            ("admin", new[] { "orders.view", "orders.edit", "reports.view", "users.roles.manage", "roles.manage", "users.block", "audit.view" }),
        })
        {
            _appRoles[MANAGER_1C][role] = new AppRole(Guid.NewGuid(), role, role, AppRoleKinds.ROLE, null, true);
            _composites[$"{MANAGER_1C}/{role}"] = [.. permissions];
        }

        _appRoles[MANAGER_1C][AppRoles.ACCESS_DENIED] = new AppRole(Guid.NewGuid(), AppRoles.ACCESS_DENIED, null, AppRoleKinds.ROLE, null, false);

        foreach (string app in Apps)
        {
            _appRoles[app] = new(StringComparer.Ordinal) { [AppRoles.ACCESS_DENIED] = new AppRole(Guid.NewGuid(), AppRoles.ACCESS_DENIED, null, AppRoleKinds.ROLE, null, false) };
        }
    }

    public IReadOnlyCollection<AppRole> AppRolesOf(string clientId) => [.. _appRoles[clientId].Values];

    /// <summary>Своя роль приложения с правами (как созданная через админку).</summary>
    public void AddAppRole(string clientId, string name, params string[] permissions)
    {
        _appRoles.GetOrAdd(clientId, _ => new(StringComparer.Ordinal))[name] =
            new AppRole(Guid.NewGuid(), name, name, AppRoleKinds.ROLE, AppRoleKinds.SOURCE_ADMIN, false);
        _composites[$"{clientId}/{name}"] = [.. permissions];
    }

    /// <summary>Приложение, как его заводит add-app.py: access-denied и пустая роль по умолчанию (без source).</summary>
    public void AddApp(string clientId, string? defaultRole = null)
    {
        ConcurrentDictionary<string, AppRole> roles = new(StringComparer.Ordinal)
        {
            [AppRoles.ACCESS_DENIED] = new AppRole(Guid.NewGuid(), AppRoles.ACCESS_DENIED, null, AppRoleKinds.ROLE, null, false),
        };
        if (defaultRole is not null)
        {
            roles[defaultRole] = new AppRole(Guid.NewGuid(), defaultRole, defaultRole, AppRoleKinds.ROLE, null, true);
        }

        _appRoles[clientId] = roles;
    }

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
        bool access = Apps.Contains(clientId) || grant.Contains(AppRoles.ACCESS_DENIED) || revoke.Contains(AppRoles.ACCESS_DENIED);
        _accounts[userId] = access
            ? account with { DeniedApps = grant.Count > 0 ? [.. account.DeniedApps.Append(clientId)] : [.. account.DeniedApps.Where(app => app != clientId)] }
            : account.WithRolesIn(clientId, [.. account.RolesIn(clientId).Except(revoke).Union(grant)]);
        return Task.FromResult(UnitResult.Success<Error>());
    }

    public Task<Result<IReadOnlyList<AppAccessInfo>, Error>> GetAppAccessAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.TryGetValue(userId, out Account? account)
            ? Result.Success<IReadOnlyList<AppAccessInfo>, Error>(
                [.. Apps.Append(MANAGER_1C).Order(StringComparer.Ordinal).Select(app => new AppAccessInfo(app, $"Приложение {app}", !account.DeniedApps.Contains(app)))])
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

    /// <param name="ClientRoles">Роли в sandbox-api.</param>
    /// <param name="OtherApps">Роли в других приложениях: clientId → роли.</param>
    private sealed record Account(
        IdentityUser User,
        IReadOnlyList<string> ClientRoles,
        bool HasPassword,
        IReadOnlyList<string> DeniedApps,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? OtherApps = null)
    {
        public IReadOnlyList<string> RolesIn(string clientId) =>
            string.Equals(clientId, SERVICE_APP, StringComparison.Ordinal) ? ClientRoles : OtherApps?.GetValueOrDefault(clientId) ?? [];

        public Account WithRolesIn(string clientId, IReadOnlyList<string> roles) =>
            string.Equals(clientId, SERVICE_APP, StringComparison.Ordinal)
                ? this with { ClientRoles = roles }
                : this with { OtherApps = new Dictionary<string, IReadOnlyList<string>>(OtherApps ?? new Dictionary<string, IReadOnlyList<string>>(), StringComparer.Ordinal) { [clientId] = roles } };
    }
}
