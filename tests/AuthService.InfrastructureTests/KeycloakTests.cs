using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AuthService.Core.Abstractions;
using AuthService.InfrastructureTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SharedKernel;
using Xunit;

namespace AuthService.InfrastructureTests;

/// <summary>
/// Тест с Keycloak: свои пользователи с уникальными именами (realm общий на весь набор), после теста — удаляются.
/// </summary>
public abstract class KeycloakTest(InfrastructureFixture fixture) : IAsyncLifetime
{
    protected const string PASSWORD = "Infra-Tests-1!";

    private readonly List<Guid> _users = [];

    protected InfrastructureFixture Fixture { get; } = fixture;

    protected IIdentityProvider IdentityProvider => Fixture.Services.GetRequiredService<IIdentityProvider>();

    protected IIdentityAdmin Admin => Fixture.Services.GetRequiredService<IIdentityAdmin>();

    public Task InitializeAsync() => Task.CompletedTask;

    public virtual async Task DisposeAsync()
    {
        foreach (Guid user in _users)
        {
            await Fixture.Master.DeleteUserAsync(user);
        }
    }

    // Хвост UUID v7 — случайный (начало — время).
    protected static string Unique(string prefix) => $"{prefix}-{Guid.CreateVersion7().ToString("N")[^12..]}";

    protected async Task<(Guid Id, string Username)> CreateUserAsync(string? email = null)
    {
        string username = Unique("itest");
        Guid id = await Fixture.Master.CreateUserAsync(username, PASSWORD, email);
        _users.Add(id);
        return (id, username);
    }

    protected static string Code(Error error) => error.Messages[0].Code;

    protected static T Success<T>(Result<T, Error> result) =>
        result.IsSuccess ? result.Value : throw new Xunit.Sdk.XunitException("Expected success, got " + result.Error.GetMessage());

    protected static void Success(UnitResult<Error> result)
    {
        if (result.IsFailure)
        {
            throw new Xunit.Sdk.XunitException("Expected success, got " + result.Error.GetMessage());
        }
    }
}

/// <summary>KeycloakIdentityProvider: вход code + PKCE через настоящую страницу входа, refresh с ротацией, выход.</summary>
[Collection(InfrastructureCollection.NAME)]
[Trait("Category", TestCategories.INFRASTRUCTURE)]
public sealed class KeycloakIdentityProviderTests(InfrastructureFixture fixture) : KeycloakTest(fixture)
{
    // Адрес возврата клиента sandbox-bff из realm JSON. Браузер туда не идёт: код берём из Location.
    private static readonly Uri s_callback = new("http://localhost:8080/auth/callback");

    [Fact]
    public async Task CodeWithPkce_TokensForUser()
    {
        (Guid user, string username) = await CreateUserAsync();

        (TokenSet tokens, _) = await SignInAsync(username);

        var access = new JsonWebToken(tokens.AccessToken);
        Assert.Equal(user.ToString(), access.Subject);
        Assert.Equal(Fixture.Issuer, access.Issuer);
        Assert.Equal(InfrastructureFixture.BFF_CLIENT, access.GetPayloadValue<string>("azp"));
        Assert.True(tokens.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(tokens.RefreshTokenExpiresAt > tokens.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task AuthorizationRequest_PkceS256AndState()
    {
        AuthorizationRequest request = Success(await IdentityProvider.CreateAuthorizationRequestAsync(
            s_callback, forceLogin: true, action: null, app: "sandbox-api", CancellationToken.None));

        Assert.StartsWith(Fixture.Issuer + "/protocol/openid-connect/auth", request.Url.ToString(), StringComparison.Ordinal);
        Assert.Equal("S256", KeycloakBrowser.Query(request.Url, "code_challenge_method"));
        Assert.Equal(request.State, KeycloakBrowser.Query(request.Url, "state"));
        Assert.Equal("login", KeycloakBrowser.Query(request.Url, "prompt"));
        Assert.Equal("sandbox-api", KeycloakBrowser.Query(request.Url, "platform_app"));
        Assert.NotEqual(request.CodeVerifier, KeycloakBrowser.Query(request.Url, "code_challenge"));
    }

    [Fact]
    public async Task Exchange_WrongVerifier_Rejected()
    {
        (_, string username) = await CreateUserAsync();
        AuthorizationRequest request = Success(await IdentityProvider.CreateAuthorizationRequestAsync(
            s_callback, forceLogin: false, action: null, app: null, CancellationToken.None));
        using var browser = new KeycloakBrowser();
        Uri callback = await browser.SignInAsync(request.Url, username, PASSWORD);

        Result<TokenSet, Error> exchanged = await IdentityProvider.ExchangeCodeAsync(
            KeycloakBrowser.Query(callback, "code")!, "not-the-verifier-" + request.CodeVerifier, s_callback, CancellationToken.None);

        Assert.Equal("auth.session.invalid", Code(exchanged.Error));
    }

    [Fact]
    public async Task Exchange_CodeTwice_SecondRejected()
    {
        (_, string username) = await CreateUserAsync();
        (_, (string code, string verifier)) = await SignInAsync(username);

        Result<TokenSet, Error> again = await IdentityProvider.ExchangeCodeAsync(code, verifier, s_callback, CancellationToken.None);

        Assert.Equal("auth.session.invalid", Code(again.Error));
    }

    [Fact]
    public async Task Refresh_Rotates_OldRefreshRejected()
    {
        (_, string username) = await CreateUserAsync();
        (TokenSet tokens, _) = await SignInAsync(username);

        TokenSet refreshed = Success(await IdentityProvider.RefreshAsync(tokens.RefreshToken, CancellationToken.None));
        Result<TokenSet, Error> reused = await IdentityProvider.RefreshAsync(tokens.RefreshToken, CancellationToken.None);

        Assert.NotEqual(tokens.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(new JsonWebToken(tokens.AccessToken).Subject, new JsonWebToken(refreshed.AccessToken).Subject);
        // realm: revokeRefreshToken = true, refreshTokenMaxReuse = 0 — повторный refresh-токен не принимается.
        Assert.Equal("auth.session.invalid", Code(reused.Error));
    }

    [Fact]
    public async Task Logout_SessionClosed_RefreshRejected()
    {
        (Guid user, string username) = await CreateUserAsync();
        (TokenSet tokens, _) = await SignInAsync(username);
        Assert.Single(Success(await Admin.GetSessionsAsync(user, CancellationToken.None)));

        Success(await IdentityProvider.LogoutAsync(tokens.RefreshToken, CancellationToken.None));

        Assert.Equal("auth.session.invalid", Code((await IdentityProvider.RefreshAsync(tokens.RefreshToken, CancellationToken.None)).Error));
        Assert.Empty(Success(await Admin.GetSessionsAsync(user, CancellationToken.None)));
    }

    [Fact]
    public async Task Refresh_GarbageToken_Rejected()
    {
        Result<TokenSet, Error> refreshed = await IdentityProvider.RefreshAsync("not-a-token", CancellationToken.None);

        Assert.Equal("auth.session.invalid", Code(refreshed.Error));
    }

    private async Task<(TokenSet Tokens, (string Code, string Verifier) Grant)> SignInAsync(string username)
    {
        AuthorizationRequest request = Success(await IdentityProvider.CreateAuthorizationRequestAsync(
            s_callback, forceLogin: false, action: null, app: null, CancellationToken.None));
        using var browser = new KeycloakBrowser();
        Uri callback = await browser.SignInAsync(request.Url, username, PASSWORD);

        Assert.StartsWith(s_callback.ToString(), callback.ToString(), StringComparison.Ordinal);
        Assert.Equal(request.State, KeycloakBrowser.Query(callback, "state"));
        string code = KeycloakBrowser.Query(callback, "code")!;
        TokenSet tokens = Success(await IdentityProvider.ExchangeCodeAsync(code, request.CodeVerifier, s_callback, CancellationToken.None));
        return (tokens, (code, request.CodeVerifier));
    }
}

/// <summary>KeycloakAdminClient от имени служебной учётки sandbox-bff: её права — те, что даёт realm JSON (FGAP v2).</summary>
[Collection(InfrastructureCollection.NAME)]
[Trait("Category", TestCategories.INFRASTRUCTURE)]
public sealed class KeycloakAdminClientTests(InfrastructureFixture fixture) : KeycloakTest(fixture)
{
    private readonly List<string> _roles = [];

    public override async Task DisposeAsync()
    {
        foreach (string role in _roles)
        {
            await Admin.DeleteAppRoleAsync(InfrastructureFixture.API_CLIENT, role, CancellationToken.None);
        }

        await base.DisposeAsync();
    }

    [Fact]
    public async Task Users_Get_FindExact_Search()
    {
        string email = Unique("mail") + "@sandbox.test";
        (Guid id, string username) = await CreateUserAsync(email);

        IdentityUser user = Success(await Admin.GetUserAsync(id, CancellationToken.None));
        Assert.Equal(username, user.Username);
        Assert.Equal(email, user.Email);
        Assert.True(user.Enabled);

        Assert.Equal(id, Success(await Admin.FindUserExactAsync(username, CancellationToken.None))!.Id);
        Assert.Equal(id, Success(await Admin.FindUserExactAsync(email, CancellationToken.None))!.Id);
        Assert.Null(Success(await Admin.FindUserExactAsync(username[..^1], CancellationToken.None)));
        Assert.Equal([id], Success(await Admin.SearchUsersAsync(username, 0, 10, CancellationToken.None)).Select(found => found.Id));
    }

    [Fact]
    public async Task Users_UnknownId_NotFound()
    {
        Result<IdentityUser, Error> user = await Admin.GetUserAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal("users.not_found", Code(user.Error));
    }

    [Fact]
    public async Task Users_Update_BlockWithReasonAndAppMode()
    {
        (Guid id, _) = await CreateUserAsync();

        Success(await Admin.UpdateUserAsync(
            id,
            new UserChanges(false, new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [UserAttributes.RESTRICTION_REASON] = "спам",
                [UserAttributes.AppModeOf(InfrastructureFixture.API_CLIENT)] = "manager",
            }),
            CancellationToken.None));

        IdentityUser blocked = Success(await Admin.GetUserAsync(id, CancellationToken.None));
        Assert.False(blocked.Enabled);
        Assert.Equal("спам", blocked.RestrictionReason);
        Assert.Equal("manager", blocked.AppModes![InfrastructureFixture.API_CLIENT]);

        // null — атрибут убрать.
        Success(await Admin.UpdateUserAsync(
            id,
            new UserChanges(true, new Dictionary<string, string?>(StringComparer.Ordinal) { [UserAttributes.RESTRICTION_REASON] = null }),
            CancellationToken.None));

        IdentityUser unblocked = Success(await Admin.GetUserAsync(id, CancellationToken.None));
        Assert.True(unblocked.Enabled);
        Assert.Null(unblocked.RestrictionReason);
    }

    [Fact]
    public async Task Roles_GrantRevoke_EffectiveIncludesPermissions()
    {
        (Guid id, _) = await CreateUserAsync();

        Success(await Admin.SetClientRolesAsync(id, InfrastructureFixture.API_CLIENT, ["manager"], [], CancellationToken.None));

        Assert.Contains("manager", Success(await Admin.GetClientRolesAsync(id, InfrastructureFixture.API_CLIENT, CancellationToken.None)));
        IReadOnlyList<string> effective = Success(await Admin.GetEffectiveClientRolesAsync(id, InfrastructureFixture.API_CLIENT, CancellationToken.None));
        Assert.Contains("users.view", effective);
        Assert.Contains("users.block", effective);
        Assert.Contains("customer", effective); // роль по умолчанию realm
        IdentityUser manager = Assert.Single(
            Success(await Admin.GetRoleUsersAsync(InfrastructureFixture.API_CLIENT, "manager", 0, 1000, CancellationToken.None)),
            found => found.Id == id);
        Assert.Equal(id, manager.Id);

        Success(await Admin.SetClientRolesAsync(id, InfrastructureFixture.API_CLIENT, [], ["manager"], CancellationToken.None));

        Assert.DoesNotContain("manager", Success(await Admin.GetClientRolesAsync(id, InfrastructureFixture.API_CLIENT, CancellationToken.None)));
        Assert.DoesNotContain("users.view", Success(await Admin.GetEffectiveClientRolesAsync(id, InfrastructureFixture.API_CLIENT, CancellationToken.None)));
    }

    [Fact]
    public async Task Clients_OnlyAppsGrantedToServiceAccount()
    {
        IReadOnlyList<ClientInfo> clients = Success(await Admin.GetClientsAsync(CancellationToken.None));

        Assert.Contains(clients, client => client.ClientId == InfrastructureFixture.API_CLIENT);
        Assert.Contains(clients, client => client.ClientId == "manager-1c");
        // Admin API других клиентов (и самого sandbox-bff с его секретом) служебной учётке не видно.
        Assert.DoesNotContain(clients, client => client.ClientId == "legacy-import");
    }

    [Fact]
    public async Task Roles_UnknownApp_NotFound()
    {
        Result<IReadOnlyList<AppRole>, Error> roles = await Admin.GetAppRolesAsync("no-such-app", CancellationToken.None);

        Assert.Equal("apps.not_found", Code(roles.Error));
    }

    [Fact]
    public async Task AppRoles_Create_Compose_Update_Delete()
    {
        string name = Unique("role");
        _roles.Add(name);
        var role = new AppRole(null, name, "Тестовая роль", AppRoleKinds.ROLE, AppRoleKinds.SOURCE_ADMIN, Builtin: false, Version: 1);

        Success(await Admin.CreateAppRoleAsync(InfrastructureFixture.API_CLIENT, role, CancellationToken.None));
        Assert.Equal("roles.exists", Code((await Admin.CreateAppRoleAsync(InfrastructureFixture.API_CLIENT, role, CancellationToken.None)).Error));

        AppRole created = Assert.Single(
            Success(await Admin.GetAppRolesAsync(InfrastructureFixture.API_CLIENT, CancellationToken.None)), found => found.Name == name);
        Assert.NotNull(created.Id);
        Assert.Equal("Тестовая роль", created.Description);
        Assert.Equal(AppRoleKinds.ROLE, created.Kind);
        Assert.Equal(AppRoleKinds.SOURCE_ADMIN, created.Source);
        Assert.False(created.Builtin);
        Assert.Equal(1, created.Version);

        // Права роли — её состав из ролей того же приложения.
        Success(await Admin.SetRoleCompositesAsync(
            InfrastructureFixture.API_CLIENT, name, ["users.view", "audit.view"], [], CancellationToken.None));
        Assert.Equal(["audit.view", "users.view"], Success(await Admin.GetRoleCompositesAsync(InfrastructureFixture.API_CLIENT, name, CancellationToken.None)));
        Success(await Admin.SetRoleCompositesAsync(InfrastructureFixture.API_CLIENT, name, [], ["audit.view"], CancellationToken.None));
        Assert.Equal(["users.view"], Success(await Admin.GetRoleCompositesAsync(InfrastructureFixture.API_CLIENT, name, CancellationToken.None)));

        Success(await Admin.UpdateAppRoleAsync(InfrastructureFixture.API_CLIENT, role with { Description = "Другое описание", Version = 2 }, CancellationToken.None));
        AppRole updated = Assert.Single(
            Success(await Admin.GetAppRolesAsync(InfrastructureFixture.API_CLIENT, CancellationToken.None)), found => found.Name == name);
        Assert.Equal("Другое описание", updated.Description);
        Assert.Equal(2, updated.Version);

        Success(await Admin.DeleteAppRoleAsync(InfrastructureFixture.API_CLIENT, name, CancellationToken.None));
        Assert.DoesNotContain(
            Success(await Admin.GetAppRolesAsync(InfrastructureFixture.API_CLIENT, CancellationToken.None)), found => found.Name == name);
    }

    [Fact]
    public async Task Credentials_PasswordOnly()
    {
        (Guid id, _) = await CreateUserAsync();

        Assert.True(Success(await Admin.HasPasswordAsync(id, CancellationToken.None)));
        Assert.Equal([Credentials.PASSWORD], Success(await Admin.GetCredentialTypesAsync(id, CancellationToken.None)));
        Assert.Equal(0, Success(await Admin.RemoveCredentialsAsync(id, Credentials.TwoFactor, CancellationToken.None)));
    }

    [Fact]
    public async Task Sessions_LogoutUser_ClosesAll()
    {
        (Guid id, string username) = await CreateUserAsync();
        await SignInAsync(username);
        await SignInAsync(username);
        IReadOnlyList<IdentitySession> sessions = Success(await Admin.GetSessionsAsync(id, CancellationToken.None));
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.Contains(InfrastructureFixture.BFF_CLIENT, session.Apps));

        Success(await Admin.DeleteSessionAsync(sessions[0].Id, CancellationToken.None));
        Assert.Equal([sessions[1].Id], Success(await Admin.GetSessionsAsync(id, CancellationToken.None)).Select(session => session.Id));

        Success(await Admin.LogoutUserAsync(id, CancellationToken.None));
        Assert.Empty(Success(await Admin.GetSessionsAsync(id, CancellationToken.None)));
    }

    // Отдельный браузер — отдельная сессия Keycloak.
    private async Task SignInAsync(string username)
    {
        Uri callback = new("http://localhost:8080/auth/callback");
        AuthorizationRequest request = Success(await IdentityProvider.CreateAuthorizationRequestAsync(
            callback, forceLogin: false, action: null, app: null, CancellationToken.None));
        using var browser = new KeycloakBrowser();
        Uri returned = await browser.SignInAsync(request.Url, username, PASSWORD);
        Success(await IdentityProvider.ExchangeCodeAsync(
            KeycloakBrowser.Query(returned, "code")!, request.CodeVerifier, callback, CancellationToken.None));
    }
}

/// <summary>
/// Ключи сервисов (KeycloakServiceRegistry): регистрация клиента по разрешению техника, вход ключом (client_credentials),
/// перевыпуск и удаление токеном управления клиентом.
/// </summary>
[Collection(InfrastructureCollection.NAME)]
[Trait("Category", TestCategories.INFRASTRUCTURE)]
public sealed class KeycloakServiceRegistryTests(InfrastructureFixture fixture) : KeycloakTest(fixture)
{
    private readonly string _clientId = "service-" + Unique("it");

    private IServiceRegistry Registry => Fixture.Services.GetRequiredService<IServiceRegistry>();

    public override async Task DisposeAsync()
    {
        await Fixture.Master.DeleteClientAsync(_clientId);
        await base.DisposeAsync();
    }

    [Fact]
    public async Task Register_Rotate_Delete()
    {
        Assert.True(Registry.IsConfigured);

        RegisteredService registered = Success(await Registry.RegisterAsync(_clientId, "Тестовый сервис", CancellationToken.None));

        JsonObject client = (await Fixture.Master.FindClientAsync(_clientId))!;
        Assert.Equal("Тестовый сервис", client["name"]!.GetValue<string>());
        Assert.True(client["serviceAccountsEnabled"]!.GetValue<bool>());
        Assert.False(client["standardFlowEnabled"]!.GetValue<bool>());
        string token = await ClientCredentialsAsync(registered.ClientSecret);
        Assert.Equal(registered.ServiceAccountId.ToString(), new JsonWebToken(token).Subject);

        RotatedSecret rotated = Success(await Registry.RotateSecretAsync(_clientId, registered.RegistrationToken, CancellationToken.None));

        Assert.NotEqual(registered.ClientSecret, rotated.ClientSecret);
        await ClientCredentialsAsync(rotated.ClientSecret);
        await Assert.ThrowsAsync<HttpRequestException>(() => ClientCredentialsAsync(registered.ClientSecret));

        Success(await Registry.DeleteAsync(_clientId, rotated.RegistrationToken, CancellationToken.None));

        Assert.Null(await Fixture.Master.FindClientAsync(_clientId));
    }

    [Fact]
    public async Task Register_ExistingClient_Conflict()
    {
        Success(await Registry.RegisterAsync(_clientId, "Тестовый сервис", CancellationToken.None));

        Result<RegisteredService, Error> again = await Registry.RegisterAsync(_clientId, "Тестовый сервис", CancellationToken.None);

        Assert.Equal("services.exists", Code(again.Error));
    }

    [Fact]
    public async Task Rotate_WrongManagementToken_Rejected()
    {
        Success(await Registry.RegisterAsync(_clientId, "Тестовый сервис", CancellationToken.None));

        Result<RotatedSecret, Error> rotated = await Registry.RotateSecretAsync(_clientId, "not-a-token", CancellationToken.None);

        Assert.Equal(ServiceRegistryErrors.MANAGEMENT_REJECTED, Code(rotated.Error));
    }

    private async Task<string> ClientCredentialsAsync(string secret)
    {
        using var http = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _clientId,
            ["client_secret"] = secret,
        });
        using HttpResponseMessage response = await http.PostAsync(new Uri(Fixture.Issuer + "/protocol/openid-connect/token"), form);
        response.EnsureSuccessStatusCode();
        JsonObject? body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return body!["access_token"]!.GetValue<string>();
    }
}
