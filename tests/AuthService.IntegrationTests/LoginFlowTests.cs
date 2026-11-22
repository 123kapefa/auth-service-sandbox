using System.Net;
using System.Text.Json;
using AuthService.Core;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using AuthService.Domain.Sessions;
using AuthService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using PlatformAuth;
using PlatformAuth.Csrf;
using SharedKernel;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Вход, обновление и выход через эндпоинты /auth/* с подставным сервером входа.</summary>
public sealed class LoginFlowTests : IClassFixture<AuthServiceWebFactory>
{
    private const string LOGIN_COOKIE = "auth_login";

    private readonly AuthServiceWebFactory _factory;
    private readonly FakeIdentityProvider _keycloak;

    public LoginFlowTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _keycloak = factory.IdentityProvider;
        _keycloak.Reset();
        factory.LoginDevices.Reset();
    }

    [Fact]
    public async Task Login_RedirectsToLoginPage_AndRemembersState()
    {
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri("/auth/login?return_to=/me/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(_keycloak.LastAuthorization!.Url, response.Headers.Location);
        Assert.Equal("http://localhost/auth/callback", QueryHelpers.ParseQuery(response.Headers.Location!.Query)["redirect_uri"]);
        SetCookieHeaderValue state = SetCookie(response, LOGIN_COOKIE);
        Assert.Equal("/auth/", state.Path.ToString());
    }

    [Fact]
    public async Task Reauth_AsksLoginPageToLoginAgain()
    {
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage plain = await browser.GetAsync(new Uri("/auth/login?return_to=/", UriKind.Relative));
        HttpResponseMessage reauth = await browser.GetAsync(new Uri("/auth/login?return_to=/&reauth=true", UriKind.Relative));

        Assert.False(QueryHelpers.ParseQuery(plain.Headers.Location!.Query).ContainsKey("prompt"));
        Assert.Equal("login", QueryHelpers.ParseQuery(reauth.Headers.Location!.Query)["prompt"]);
    }

    [Theory]
    [InlineData("update-password", AccountAction.UpdatePassword)]
    [InlineData("update-email", AccountAction.UpdateEmail)]
    [InlineData("configure-totp", AccountAction.ConfigureTotp)]
    [InlineData("register-passkey", AccountAction.RegisterPasskey)]
    [InlineData("configure-recovery-codes", AccountAction.ConfigureRecoveryCodes)]
    [InlineData("update-phone", AccountAction.UpdatePhone)]
    public async Task Login_WithAccountAction_AsksLoginPageForAction(string action, AccountAction expected)
    {
        using HttpClient browser = _factory.CreateBrowserClient();

        // credential у действий, кроме удаления, не используется — не уходит и в Keycloak.
        HttpResponseMessage response = await browser.GetAsync(
            new Uri($"/auth/login?return_to=/account/&action={action}&credential=4b4f2c3e-8f1a-4d7e-9a51-0d6a1c2b3e4f", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new AccountActionRequest(expected), _keycloak.LastAction);
    }

    [Theory]
    [InlineData("manager-1c", "manager-1c")]
    [InlineData("orders-web", "orders-web")]
    [InlineData("evil/../x", null)]
    [InlineData("", null)]
    public async Task Login_WithApp_PassesAppForBranding_InvalidIgnored(string app, string? expected)
    {
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(
            new Uri($"/auth/login?return_to=/account/&app={Uri.EscapeDataString(app)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, _keycloak.LastApp);
    }

    [Fact]
    public async Task Login_DeleteCredential_PassesCanonicalCredentialId()
    {
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri(
            "/auth/login?return_to=/account/security&action=delete-credential&credential=4B4F2C3E-8F1A-4D7E-9A51-0D6A1C2B3E4F",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            new AccountActionRequest(AccountAction.DeleteCredential, "4b4f2c3e-8f1a-4d7e-9a51-0d6a1c2b3e4f"),
            _keycloak.LastAction);
    }

    [Theory]
    [InlineData("")]
    [InlineData("&credential=")]
    [InlineData("&credential=password")]
    [InlineData("&credential=x%26kc_action%3DUPDATE_PASSWORD")]
    [InlineData("&credential=%7B0x4b4f2c3e%2C0x8f1a%2C0x4d7e%2C%7B0x9a%2C0x51%2C0x0d%2C0x6a%2C0x1c%2C0x2b%2C0x3e%2C0x4f%7D%7D")]
    public async Task Login_DeleteCredential_WithoutValidId_Rejected(string credential)
    {
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(
            new Uri($"/auth/login?return_to=/account/&action=delete-credential{credential}", UriKind.Relative));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "auth.action.credential_required");
        Assert.Null(_keycloak.LastAuthorization);
    }

    [Fact]
    public async Task Login_UnknownAccountAction_Rejected_NoLoginStarted()
    {
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri("/auth/login?return_to=/account/&action=delete_account", UriKind.Relative));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "auth.action.unknown");
        Assert.Null(_keycloak.LastAuthorization);
    }

    [Theory]
    [InlineData("/account/", "success", "/account/?action_status=success")]
    [InlineData("/account/?tab=profile", "cancelled", "/account/?tab=profile&action_status=cancelled")]
    [InlineData("/account/#email", "error", "/account/?action_status=error#email")]
    [InlineData("/account/", "evil<script>", "/account/")]
    [InlineData("/account/?action=update-password&action_status=success", "cancelled", "/account/?action=update-password&action_status=cancelled")]
    [InlineData("/account/?action_status=success", "evil", "/account/")]
    public async Task Callback_AccountActionStatus_PassedToReturnTo(string returnTo, string status, string expected)
    {
        (HttpClient browser, string loginCookie) = await StartLoginAsync(returnTo);

        HttpResponseMessage response = await Callback(
            browser, loginCookie, $"code=c&state={_keycloak.LastAuthorization!.State}&kc_action_status={Uri.EscapeDataString(status)}");

        Assert.Equal(expected, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_ExchangesCodeWithVerifier_SetsCookies_AndReturnsToApp()
    {
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/me/");

        HttpResponseMessage response = await Callback(browser, loginCookie, $"code=the-code&state={_keycloak.LastAuthorization!.State}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/me/", response.Headers.Location!.ToString());
        (string code, string verifier, Uri redirectUri) = Assert.Single(_keycloak.Exchanges);
        Assert.Equal("the-code", code);
        Assert.Equal(_keycloak.LastAuthorization.CodeVerifier, verifier);
        Assert.Equal("http://localhost/auth/callback", redirectUri.ToString());
        Assert.Equal("access-1", SetCookie(response, AuthCookies.ACCESS_TOKEN).Value.ToString());
        Assert.Equal("/auth/", SetCookie(response, AuthCookies.REFRESH_TOKEN).Path.ToString());
    }

    [Fact]
    public async Task Callback_AfterPhoneChange_AuditedOnce_ByThePerson()
    {
        _factory.AuditLog.Reset();
        _factory.IdentityAdmin.Add(TestUser.Customer);
        _factory.IdentityAdmin.SetPhoneChanged(TestUser.Customer, "+79990000001", "+79990000002", DateTimeOffset.UtcNow.AddSeconds(-5));
        _keycloak.TokenResult = FakeIdentityProvider.Tokens(_factory.CreateToken(TestUser.Customer), "refresh-1");

        for (int i = 0; i < 2; i++)
        {
            (HttpClient browser, string loginCookie) = await StartActionAsync("update-phone");
            await Callback(browser, loginCookie, $"code=c&state={_keycloak.LastAuthorization!.State}&kc_action_status=success");
        }

        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal((AuditActions.USER_PHONE_CHANGED, TestUser.Customer.Id, TestUser.Customer.Id), (entry.Action, entry.ObjectId, entry.ActorId!.Value));
        using JsonDocument changes = JsonDocument.Parse(entry.Changes);
        Assert.Equal("+79990000001", changes.RootElement.GetProperty("phone").GetProperty("old").GetString());
        Assert.Equal("+79990000002", changes.RootElement.GetProperty("phone").GetProperty("new").GetString());
    }

    [Theory]
    [InlineData("success", null)]
    [InlineData("cancelled", "password")]
    public async Task Callback_AfterCredentialDeleted_SuspendedSignInMethodsForgotten(string status, string? left)
    {
        // Пароль выключали при ключе доступа; человек удалил ключ — выключение больше не действует и не должно вернуться.
        _factory.IdentityAdmin.Add(TestUser.Customer);
        _factory.IdentityAdmin.SetDisabledSignInMethods(TestUser.Customer, "password");
        _keycloak.TokenResult = FakeIdentityProvider.Tokens(_factory.CreateToken(TestUser.Customer), "refresh-1");
        HttpClient browser = _factory.CreateBrowserClient();
        HttpResponseMessage login = await browser.GetAsync(new Uri(
            "/auth/login?return_to=/account/&action=delete-credential&credential=4B4F2C3E-8F1A-4D7E-9A51-0D6A1C2B3E4F", UriKind.Relative));

        HttpResponseMessage response = await Callback(
            browser, SetCookie(login, LOGIN_COOKIE).Value.ToString(), $"code=c&state={_keycloak.LastAuthorization!.State}&kc_action_status={status}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(left, (await _factory.IdentityAdmin.GetUserAsync(TestUser.Customer.Id, CancellationToken.None)).Value.DisabledSignInMethods);
    }

    [Theory]
    [InlineData("update-phone", "cancelled")]
    [InlineData("update-email", "success")]
    [InlineData(null, null)]
    public async Task Callback_NotAPhoneChange_NotAudited(string? action, string? status)
    {
        _factory.AuditLog.Reset();
        _factory.IdentityAdmin.Add(TestUser.Customer);
        _factory.IdentityAdmin.SetPhoneChanged(TestUser.Customer, "+79990000001", "+79990000002", DateTimeOffset.UtcNow.AddSeconds(-5));
        _keycloak.TokenResult = FakeIdentityProvider.Tokens(_factory.CreateToken(TestUser.Customer), "refresh-1");

        (HttpClient browser, string loginCookie) = action is null ? await StartLoginAsync("/account/") : await StartActionAsync(action);
        string query = $"code=c&state={_keycloak.LastAuthorization!.State}" + (status is null ? "" : $"&kc_action_status={status}");
        await Callback(browser, loginCookie, query);

        Assert.Empty(_factory.AuditLog.Entries);
    }

    [Fact]
    public async Task Callback_RecordsBrowserOfSession()
    {
        _keycloak.TokenResult = FakeIdentityProvider.Tokens(_factory.CreateToken(TestUser.Customer, sessionId: "s-new"), "refresh-1");
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/account/");

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri($"/auth/callback?code=c&state={_keycloak.LastAuthorization!.State}", UriKind.Relative));
        request.Headers.Add("Cookie", $"{LOGIN_COOKIE}={loginCookie}");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64; rv:143.0) Gecko/20100101 Firefox/143.0");
        HttpResponseMessage response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        LoginDevice device = Assert.Single(_factory.LoginDevices.Devices);
        Assert.Equal(("s-new", TestUser.Customer.Id), (device.SessionId, device.UserId));
        Assert.Contains("Firefox/143.0", device.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Callback_DevicesUnavailable_LoginStillCompletes()
    {
        _factory.LoginDevices.Broken = true;
        _keycloak.TokenResult = FakeIdentityProvider.Tokens(_factory.CreateToken(TestUser.Customer, sessionId: "s-new"), "refresh-1");
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/account/");

        HttpResponseMessage response = await Callback(browser, loginCookie, $"code=c&state={_keycloak.LastAuthorization!.State}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/", response.Headers.Location!.ToString());
        Assert.Empty(_factory.LoginDevices.Devices);
    }

    [Fact]
    public async Task Callback_ForeignReturnTo_ReturnsToDefault()
    {
        (HttpClient browser, string loginCookie) = await StartLoginAsync("https://evil.example/phish");

        HttpResponseMessage response = await Callback(browser, loginCookie, $"code=c&state={_keycloak.LastAuthorization!.State}");

        Assert.Equal("/account/", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/\t/evil.example")]     // браузер читает это как //evil.example
    [InlineData("/\r/evil.example")]     // раньше — 500 (CR в заголовке Location)
    [InlineData("/\n/evil.example")]
    [InlineData("/\u200B/evil.example")]
    public async Task Callback_ReturnToWithHiddenCharacters_ReturnsToDefault(string returnTo)
    {
        (HttpClient browser, string loginCookie) = await StartLoginAsync(returnTo);

        HttpResponseMessage response = await Callback(browser, loginCookie, $"code=c&state={_keycloak.LastAuthorization!.State}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_NonAsciiReturnTo_LocationEncoded_NotServerError()
    {
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/заказы/1");

        HttpResponseMessage response = await Callback(browser, loginCookie, $"code=c&state={_keycloak.LastAuthorization!.State}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/%D0%B7%D0%B0%D0%BA%D0%B0%D0%B7%D1%8B/1", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Callback_WrongState_Rejected_NoTokens()
    {
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/");

        HttpResponseMessage response = await Callback(browser, loginCookie, "code=c&state=forged");

        AssertLostLoginRestarts(response);
        Assert.Empty(_keycloak.Exchanges);
        Assert.DoesNotContain(SetCookies(response), cookie => cookie.Name == AuthCookies.ACCESS_TOKEN);
    }

    [Fact]
    public async Task Callback_WithoutStartedLogin_GoesHome_WithoutTokens()
    {
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync(new Uri("/auth/callback?code=c&state=s", UriKind.Relative));

        AssertLostLoginRestarts(response);
        Assert.Empty(_keycloak.Exchanges);
    }

    // Вход, который сервис не узнал: не JSON с ошибкой, а страница по умолчанию — без токенов, код не обменян.
    private static void AssertLostLoginRestarts(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(SetCookies(response), cookie => cookie.Name == AuthCookies.ACCESS_TOKEN);
    }

    [Fact]
    public async Task Callback_LoginStateIsSingleUse()
    {
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/");
        string query = $"code=c&state={_keycloak.LastAuthorization!.State}";

        HttpResponseMessage first = await Callback(browser, loginCookie, query);
        Assert.True(SetCookie(first, LOGIN_COOKIE).Expires < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Callback_UserCancelled_IsDenied()
    {
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/");

        HttpResponseMessage response = await Callback(browser, loginCookie, "error=access_denied&error_description=user+cancelled");

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "auth.login.denied");
    }

    [Fact]
    public async Task Callback_MissingCode_IsValidationError()
    {
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/");

        HttpResponseMessage response = await Callback(browser, loginCookie, $"state={_keycloak.LastAuthorization!.State}");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "value.is.required");
    }

    [Fact]
    public async Task Refresh_RotatesTokensInCookies()
    {
        _keycloak.TokenResult = FakeIdentityProvider.Tokens("access-2", "refresh-2");
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage response = await PostWithRefreshCookie(browser, "/auth/refresh", "refresh-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["refresh-1"], _keycloak.Refreshes);
        Assert.Equal("access-2", SetCookie(response, AuthCookies.ACCESS_TOKEN).Value.ToString());
        Assert.Equal("refresh-2", SetCookie(response, AuthCookies.REFRESH_TOKEN).Value.ToString());
    }

    [Fact]
    public async Task Refresh_Rejected_ClearsCookies_401()
    {
        _keycloak.TokenResult = Result.Failure<TokenSet, Error>(IdentityProviderErrors.GrantRejected());
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage response = await PostWithRefreshCookie(browser, "/auth/refresh", "stale");

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "auth.session.invalid");
        Assert.True(SetCookie(response, AuthCookies.ACCESS_TOKEN).Expires < DateTimeOffset.UtcNow);
        Assert.True(SetCookie(response, AuthCookies.REFRESH_TOKEN).Expires < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_401()
    {
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage response = await browser.PostAsync(new Uri("/auth/refresh", UriKind.Relative), content: null);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "auth.session.missing");
        Assert.Empty(_keycloak.Refreshes);
    }

    [Fact]
    public async Task Logout_EndsSessionInKeycloak_AndClearsCookies()
    {
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage response = await PostWithRefreshCookie(browser, "/auth/logout", "refresh-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["refresh-1"], _keycloak.Logouts);
        Assert.True(SetCookie(response, AuthCookies.ACCESS_TOKEN).Expires < DateTimeOffset.UtcNow);
        Assert.True(SetCookie(response, AuthCookies.REFRESH_TOKEN).Expires < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Logout_KeycloakDown_StillClearsCookies()
    {
        _keycloak.LogoutResult = UnitResult.Failure(IdentityProviderErrors.Unavailable());
        using HttpClient browser = _factory.CreateBrowserClient();

        HttpResponseMessage response = await PostWithRefreshCookie(browser, "/auth/logout", "refresh-1");

        await AssertErrorAsync(response, HttpStatusCode.InternalServerError, "auth.provider.unavailable");
        Assert.True(SetCookie(response, AuthCookies.REFRESH_TOKEN).Expires < DateTimeOffset.UtcNow);
    }

    // Метрики входа (13.1): счётчики общие на процесс, поэтому проверяется «не меньше одного» по набору меток.
    [Fact]
    public async Task Metrics_LoginStartedAndSucceeded_Counted()
    {
        using var metrics = new MetricsRecorder(AuthMetrics.METER_NAME);
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/me/");

        await Callback(browser, loginCookie, $"code=the-code&state={_keycloak.LastAuthorization!.State}");

        Assert.True(metrics.Sum("logins_started") >= 1);
        Assert.True(metrics.Sum("logins", ("result", "success")) >= 1);
    }

    [Theory]
    [InlineData("code=c&state=forged", "state_lost")]
    [InlineData("error=access_denied&error_description=user+cancelled", "denied")]
    public async Task Metrics_FailedLogin_CountedByReason(string query, string result)
    {
        using var metrics = new MetricsRecorder(AuthMetrics.METER_NAME);
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/");

        await Callback(browser, loginCookie, query);

        Assert.True(metrics.Sum("logins", ("result", result)) >= 1);
        Assert.Equal(0, metrics.Sum("logins", ("result", "success")));
    }

    [Fact]
    public async Task Metrics_MissingCode_CountedAsInvalid_ExchangeRejected_CountedSeparately()
    {
        using var metrics = new MetricsRecorder(AuthMetrics.METER_NAME);
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/");
        await Callback(browser, loginCookie, $"state={_keycloak.LastAuthorization!.State}");

        _keycloak.TokenResult = Result.Failure<TokenSet, Error>(IdentityProviderErrors.GrantRejected());
        (HttpClient second, string secondCookie) = await StartLoginAsync("/");
        await Callback(second, secondCookie, $"code=bad&state={_keycloak.LastAuthorization.State}");
        _keycloak.TokenResult = FakeIdentityProvider.Tokens("access-1", "refresh-1");

        Assert.True(metrics.Sum("logins", ("result", "invalid")) >= 1);
        Assert.True(metrics.Sum("logins", ("result", "exchange_rejected")) >= 1);
    }

    [Fact]
    public async Task Metrics_Refresh_CountedByResult()
    {
        using var metrics = new MetricsRecorder(AuthMetrics.METER_NAME);
        using HttpClient browser = _factory.CreateBrowserClient();

        _keycloak.TokenResult = FakeIdentityProvider.Tokens("access-2", "refresh-2");
        await PostWithRefreshCookie(browser, "/auth/refresh", "refresh-1");
        _keycloak.TokenResult = Result.Failure<TokenSet, Error>(IdentityProviderErrors.GrantRejected());
        await PostWithRefreshCookie(browser, "/auth/refresh", "stale");
        await browser.PostAsync(new Uri("/auth/refresh", UriKind.Relative), content: null);
        _keycloak.TokenResult = FakeIdentityProvider.Tokens("access-1", "refresh-1");

        Assert.True(metrics.Sum("refreshes", ("result", "ok")) >= 1);
        Assert.True(metrics.Sum("refreshes", ("result", "rejected")) >= 1);
        Assert.True(metrics.Sum("refreshes", ("result", "no_session")) >= 1);
    }

    [Fact]
    public async Task Metrics_Logout_CountedOkAndError()
    {
        using var metrics = new MetricsRecorder(AuthMetrics.METER_NAME);
        using HttpClient browser = _factory.CreateBrowserClient();

        await PostWithRefreshCookie(browser, "/auth/logout", "refresh-1");
        _keycloak.LogoutResult = UnitResult.Failure(IdentityProviderErrors.Unavailable());
        await PostWithRefreshCookie(browser, "/auth/logout", "refresh-1");
        _keycloak.LogoutResult = UnitResult.Success<Error>();

        Assert.True(metrics.Sum("logouts", ("result", "ok")) >= 1);
        Assert.True(metrics.Sum("logouts", ("result", "error")) >= 1);
    }

    [Fact]
    public async Task Metrics_Labels_NeverCarryTokensOrCodes()
    {
        using var metrics = new MetricsRecorder(AuthMetrics.METER_NAME);
        (HttpClient browser, string loginCookie) = await StartLoginAsync("/me/");
        await Callback(browser, loginCookie, $"code=secret-code-123&state={_keycloak.LastAuthorization!.State}");
        await PostWithRefreshCookie(browser, "/auth/refresh", "refresh-secret-456");

        string?[] labels = [.. metrics.TagValues("logins"), .. metrics.TagValues("refreshes")];
        Assert.DoesNotContain(labels, label => label is not null && (label.Contains("secret", StringComparison.Ordinal) || label.Contains("access", StringComparison.Ordinal)));
    }

    private async Task<(HttpClient Browser, string LoginCookie)> StartLoginAsync(string returnTo)
    {
        HttpClient browser = _factory.CreateBrowserClient();
        HttpResponseMessage login = await browser.GetAsync(
            new Uri($"/auth/login?return_to={Uri.EscapeDataString(returnTo)}", UriKind.Relative));
        return (browser, SetCookie(login, LOGIN_COOKIE).Value.ToString());
    }

    private async Task<(HttpClient Browser, string LoginCookie)> StartActionAsync(string action)
    {
        HttpClient browser = _factory.CreateBrowserClient();
        HttpResponseMessage login = await browser.GetAsync(
            new Uri($"/auth/login?return_to=/account/&action={action}", UriKind.Relative));
        return (browser, SetCookie(login, LOGIN_COOKIE).Value.ToString());
    }

    private static async Task<HttpResponseMessage> Callback(HttpClient browser, string loginCookie, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/auth/callback?{query}", UriKind.Relative));
        request.Headers.Add("Cookie", $"{LOGIN_COOKIE}={loginCookie}");
        return await browser.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostWithRefreshCookie(HttpClient browser, string path, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Headers.Add("Cookie", $"{AuthCookies.REFRESH_TOKEN}={refreshToken}");
        request.Headers.Add(CsrfHeaderMiddleware.HEADER_NAME, "XMLHttpRequest"); // как браузер: интерфейсы сервиса ставят заголовок сами
        return await browser.SendAsync(request);
    }

    private static IList<SetCookieHeaderValue> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
            ? SetCookieHeaderValue.ParseList([.. values])
            : [];

    private static SetCookieHeaderValue SetCookie(HttpResponseMessage response, string name) =>
        Assert.Single(SetCookies(response), cookie => cookie.Name == name);

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString());
    }
}
