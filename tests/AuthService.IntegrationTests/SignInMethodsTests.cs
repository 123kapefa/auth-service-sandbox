using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>
/// Способы входа человека (10.3): кабинет — GET/PUT /me/sign-in-methods, админка — просмотр и «вернуть по умолчанию».
/// С подставным Admin API, журналом и уведомлениями в памяти.
/// </summary>
public sealed class SignInMethodsTests : IClassFixture<AuthServiceWebFactory>
{
    private const string CURRENT = "s-current";
    private static readonly DateTimeOffset _created = DateTimeOffset.Parse("2026-10-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityAdmin _keycloak;

    public SignInMethodsTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityAdmin;
        _keycloak.Reset();
        factory.AuditLog.Reset();
        factory.Notices.Reset();
        factory.Revocations.Reset();
        factory.SignIn.Reset();
        _keycloak.Add(TestUser.Customer, [], email: "customer@sandbox.test");
        _keycloak.Add(TestUser.AdminInAdminMode, [AuthModes.ADMIN]);
        _keycloak.Add(TestUser.ManagerInManagerMode, [AuthModes.MANAGER]);
        _keycloak.AddSession(TestUser.Customer, CURRENT, "sandbox-bff");
        _keycloak.AddSession(TestUser.Customer, "s-phone", "sandbox-bff");
    }

    [Fact]
    public async Task Get_AllEnabled_PasswordAndPhoneNeedPasskey()
    {
        JsonElement result = await ResultAsync(await GetOwnAsync());

        Assert.Equal(
            ["password:True:True:needs_passkey_or_authenticator", "phone-code:True:True:needs_passkey_or_authenticator", "email-code:True:True:"],
            Methods(result));
        Assert.False(result.GetProperty("hasPasskey").GetBoolean());
        Assert.Empty(result.GetProperty("history").EnumerateArray());
        Assert.Empty(result.GetProperty("projectDisabled").EnumerateArray());
    }

    [Fact]
    public async Task Get_ProjectDisabled_OffAndEndedTransition_NotCurrentTransition()
    {
        Dictionary<string, ProjectSignInMethod> methods = new(FakeProjectSignIn.Default().Methods, StringComparer.Ordinal)
        {
            [ProjectSignInRules.PASSWORD] = new(ProjectSignInRules.OFF),
            [ProjectSignInRules.PHONE] = new(ProjectSignInRules.TRANSITION, new DateOnly(2000, 1, 1)),
            [ProjectSignInRules.EMAIL] = new(ProjectSignInRules.TRANSITION, new DateOnly(2999, 1, 1)),
            [ProjectSignInRules.YANDEX] = new(ProjectSignInRules.OFF, Configured: true),
        };
        _factory.SignIn.Current = FakeProjectSignIn.Default() with { Methods = methods };

        JsonElement result = await ResultAsync(await GetOwnAsync());

        Assert.Equal(
            ["phone", "password", "yandex"],
            result.GetProperty("projectDisabled").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    [Fact]
    public async Task Get_NoEmail_EmailCodeNotAvailable()
    {
        _keycloak.Add(TestUser.Customer, []);

        JsonElement result = await ResultAsync(await GetOwnAsync());

        Assert.Equal("email-code:False:True:not_available", Methods(result)[2]);
    }

    [Fact]
    public async Task DisablePassword_WithoutPasskey_Conflict_NothingSaved()
    {
        await AssertErrorAsync(await SetAsync("password", enabled: false), HttpStatusCode.Conflict, "sign_in_methods.needs_passkey_or_authenticator");

        Assert.Empty(_keycloak.AttributeChanges);
        Assert.Empty(_factory.Notices.Sent);
    }

    [Fact]
    public async Task DisablePassword_WithPasskey_Saved_Audited_Notified_OtherDevicesSignedOut()
    {
        AddPasskey();

        JsonElement result = await ResultAsync(await SetAsync("password", enabled: false));

        Assert.Equal("password:True:False:", Methods(result)[0]);
        Assert.Equal((TestUser.Customer.Id, UserAttributes.DISABLED_SIGN_IN_METHODS, "password"), Assert.Single(_keycloak.AttributeChanges));
        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal((AuditActions.USER_SIGN_IN_METHOD_CHANGED, TestUser.Customer.Id, TestUser.Customer.Id), (entry.Action, entry.ObjectId, entry.ActorId!.Value));
        Assert.Equal((TestUser.Customer.Id, new SignInMethodNotice("disabled", "password")), Assert.Single(_factory.Notices.Sent));
        Assert.Equal(["s-phone"], _keycloak.DeletedSessions);

        JsonElement change = Assert.Single(result.GetProperty("history").EnumerateArray());
        Assert.Equal(("password", false, false), (change.GetProperty("method").GetString(), change.GetProperty("enabled").GetBoolean(), change.GetProperty("byAdmin").GetBoolean()));
    }

    [Fact]
    public async Task Disable_StayOnOtherDevices_SessionsKept()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetAsync("email-code", enabled: false, signOutOtherDevices: false)).StatusCode);

        Assert.Empty(_keycloak.DeletedSessions);
    }

    [Fact]
    public async Task DisableAll_WithPasskey_Ok()
    {
        AddPasskey();
        await ResultAsync(await SetAsync("password", enabled: false));
        await ResultAsync(await SetAsync("phone-code", enabled: false));

        JsonElement result = await ResultAsync(await SetAsync("email-code", enabled: false));

        Assert.All(result.GetProperty("methods").EnumerateArray(), method => Assert.False(method.GetProperty("enabled").GetBoolean()));
        Assert.Equal("password,phone-code,email-code", _keycloak.AttributeChanges[^1].Value);
    }

    [Fact]
    public async Task DisableLast_OnlyAuthenticatorLeft_Conflict()
    {
        AddCredential(Credentials.OTP);
        AddCredential(Credentials.RECOVERY_CODES);
        await ResultAsync(await SetAsync("password", enabled: false));
        await ResultAsync(await SetAsync("phone-code", enabled: false));

        // Аутентификатор — второй шаг, а не способ входа: без кода на почту войти было бы нечем.
        await AssertErrorAsync(await SetAsync("email-code", enabled: false), HttpStatusCode.Conflict, "sign_in_methods.last_method");
    }

    [Fact]
    public async Task DisableWithAuthenticator_NoRecoveryCodes_Conflict()
    {
        AddCredential(Credentials.OTP);

        await AssertErrorAsync(await SetAsync("password", enabled: false), HttpStatusCode.Conflict, "sign_in_methods.recovery_codes_required");
    }

    [Fact]
    public async Task Set_OldLogin_ReauthenticationRequired()
    {
        AddPasskey();

        await AssertErrorAsync(
            await SetAsync("password", enabled: false, loggedInAgo: TimeSpan.FromMinutes(10)),
            HttpStatusCode.Forbidden,
            "auth.reauthentication_required");
        Assert.Empty(_keycloak.AttributeChanges);
    }

    [Fact]
    public async Task Enable_Back_AttributeRemoved_Notified()
    {
        AddPasskey();
        _keycloak.SetDisabledSignInMethods(TestUser.Customer, "password,email-code");

        JsonElement result = await ResultAsync(await SetAsync("password", enabled: true));

        Assert.Equal(["password:True:True:", "phone-code:True:True:", "email-code:True:False:"], Methods(result));
        Assert.Equal("email-code", Assert.Single(_keycloak.AttributeChanges).Value);
        Assert.Equal(new SignInMethodNotice("enabled", "password"), Assert.Single(_factory.Notices.Sent).Notice);
        Assert.Empty(_keycloak.DeletedSessions);
    }

    [Fact]
    public async Task Set_AlreadySo_OkWithoutAuditAndNotice()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetAsync("password", enabled: true)).StatusCode);

        Assert.Empty(_keycloak.AttributeChanges);
        Assert.Empty(_factory.AuditLog.Entries);
        Assert.Empty(_factory.Notices.Sent);
    }

    [Fact]
    public async Task Disabled_PasswordWithoutPasskey_ShownEnabled()
    {
        // Ключ доступа удалили на странице Keycloak — пароль снова работает (так же решает плагин).
        _keycloak.SetDisabledSignInMethods(TestUser.Customer, "password");

        Assert.Equal("password:True:True:needs_passkey_or_authenticator", Methods(await ResultAsync(await GetOwnAsync()))[0]);
    }

    [Fact]
    public async Task Set_NoticeFails_ChangeStillSaved()
    {
        _factory.Notices.Failure = IdentityProviderErrors.Unavailable();

        Assert.Equal(HttpStatusCode.OK, (await SetAsync("email-code", enabled: false)).StatusCode);
        Assert.Equal("email-code", Assert.Single(_keycloak.AttributeChanges).Value);
    }

    [Fact]
    public async Task Set_UnknownMethod_NotFound() =>
        await AssertErrorAsync(await SetAsync("passkey", enabled: false), HttpStatusCode.NotFound, "sign_in_methods.unknown_method");

    [Fact]
    public async Task Set_NoEnabled_BadRequest()
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.Customer, DateTimeOffset.UtcNow, CURRENT);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/me/sign-in-methods/password", new { })).StatusCode);
    }

    [Fact]
    public async Task Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/me/sign-in-methods", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task AdminReset_Cleared_Audited_Notified_InHistory()
    {
        _keycloak.SetDisabledSignInMethods(TestUser.Customer, "password,phone-code");
        AddPasskey();

        JsonElement result = await ResultAsync(await AdminAsync(HttpMethod.Post, "sign-in-methods/reset"));

        Assert.All(result.GetProperty("methods").EnumerateArray(), method => Assert.True(method.GetProperty("enabled").GetBoolean()));
        Assert.Equal((TestUser.Customer.Id, UserAttributes.DISABLED_SIGN_IN_METHODS, (string?)null), Assert.Single(_keycloak.AttributeChanges));
        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal((AuditActions.USER_SIGN_IN_METHODS_RESET, TestUser.AdminInAdminMode.Id), (entry.Action, entry.ActorId!.Value));
        Assert.Equal(new SignInMethodNotice("reset", null), Assert.Single(_factory.Notices.Sent).Notice);
        Assert.True(Assert.Single(result.GetProperty("history").EnumerateArray()).GetProperty("byAdmin").GetBoolean());
    }

    [Fact]
    public async Task AdminReset_NothingDisabled_OkWithoutAudit()
    {
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(HttpMethod.Post, "sign-in-methods/reset")).StatusCode);

        Assert.Empty(_factory.AuditLog.Entries);
        Assert.Empty(_factory.Notices.Sent);
    }

    [Fact]
    public async Task AdminReset_OldLogin_ReauthenticationRequired()
    {
        _keycloak.SetDisabledSignInMethods(TestUser.Customer, "email-code");

        await AssertErrorAsync(
            await AdminAsync(HttpMethod.Post, "sign-in-methods/reset", loggedInAgo: TimeSpan.FromMinutes(10)),
            HttpStatusCode.Forbidden,
            "auth.reauthentication_required");
        Assert.Empty(_keycloak.AttributeChanges);
    }

    [Fact]
    public async Task AdminReset_Own_Forbidden() =>
        await AssertErrorAsync(
            await AdminAsync(HttpMethod.Post, "sign-in-methods/reset", target: TestUser.AdminInAdminMode),
            HttpStatusCode.Forbidden,
            "users.self_credentials");

    [Fact]
    public async Task AdminReset_Manager_Forbidden_ButCanView()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await AdminAsync(HttpMethod.Post, "sign-in-methods/reset", caller: TestUser.ManagerInManagerMode)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(HttpMethod.Get, "sign-in-methods", caller: TestUser.ManagerInManagerMode)).StatusCode);
    }

    [Fact]
    public async Task AdminPasswordReset_PasswordDisabled_Conflict()
    {
        AddPasskey();
        _keycloak.SetDisabledSignInMethods(TestUser.Customer, "password");

        await AssertErrorAsync(await AdminAsync(HttpMethod.Post, "password-reset"), HttpStatusCode.Conflict, "users.password_disabled");
        Assert.Empty(_keycloak.ActionEmails);
    }

    private void AddPasskey() => AddCredential(Credentials.PASSKEY);

    private void AddCredential(string type) =>
        _keycloak.AddCredential(TestUser.Customer, new UserCredential(type + "-1", type, null, _created));

    private static List<string> Methods(JsonElement result) =>
    [
        .. result.GetProperty("methods").EnumerateArray().Select(method =>
            $"{method.GetProperty("method").GetString()}:{method.GetProperty("available").GetBoolean()}:" +
            $"{method.GetProperty("enabled").GetBoolean()}:{method.GetProperty("disableBlocker").GetString()}"),
    ];

    private async Task<HttpResponseMessage> GetOwnAsync()
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.Customer, DateTimeOffset.UtcNow, CURRENT);
        return await client.GetAsync(new Uri("/me/sign-in-methods", UriKind.Relative));
    }

    private async Task<HttpResponseMessage> SetAsync(string method, bool enabled, bool? signOutOtherDevices = null, TimeSpan? loggedInAgo = null)
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.Customer, DateTimeOffset.UtcNow - (loggedInAgo ?? TimeSpan.Zero), CURRENT);
        return await client.PutAsJsonAsync($"/me/sign-in-methods/{method}", new { enabled, signOutOtherDevices });
    }

    private async Task<HttpResponseMessage> AdminAsync(
        HttpMethod method,
        string action,
        TimeSpan? loggedInAgo = null,
        TestUser? caller = null,
        TestUser? target = null)
    {
        using HttpClient client = _factory.CreateClientFor(caller ?? TestUser.AdminInAdminMode, DateTimeOffset.UtcNow - (loggedInAgo ?? TimeSpan.Zero));
        using var request = new HttpRequestMessage(method, new Uri($"/auth/admin/users/{(target ?? TestUser.Customer).Id}/{action}", UriKind.Relative));
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("result").Clone();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString());
    }
}
