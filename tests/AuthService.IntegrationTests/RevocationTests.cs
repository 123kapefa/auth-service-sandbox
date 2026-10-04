using System.Net;
using System.Net.Http.Json;
using AuthService.Core.Abstractions;
using AuthService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using PlatformAuth;
using SharedKernel;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Мгновенный отзыв: уже выданный access-токен перестаёт работать сразу после действия админа или выхода.</summary>
public sealed class RevocationTests : IClassFixture<AuthServiceWebFactory>
{
    private static readonly DateTimeOffset _issuedEarlier = DateTimeOffset.UtcNow.AddMinutes(-5);

    private readonly AuthServiceWebFactory _factory;

    public RevocationTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _factory.Revocations.Reset();
        _factory.IdentityProvider.Reset();
        _factory.IdentityAdmin.Reset();
        _factory.IdentityAdmin.Add(TestUser.Customer, []);
        _factory.IdentityAdmin.Add(TestUser.AdminInAdminMode, [AuthModes.ADMIN]);
    }

    [Fact]
    public async Task Block_OldTokenRejectedImmediately_NewLoginAccepted()
    {
        string oldToken = _factory.CreateToken(TestUser.Customer, issuedAt: _issuedEarlier);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(oldToken));

        await AdminAsync(HttpMethod.Put, $"/auth/admin/users/{TestUser.Customer.Id}/restriction", new { restricted = true });

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(oldToken));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(_factory.CreateToken(TestUser.Customer)));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(_factory.CreateToken(TestUser.AdminInAdminMode, issuedAt: _issuedEarlier)));
    }

    [Fact]
    public async Task Block_TokenFromTheSameSecond_RejectedViaItsSession()
    {
        // iat в секундах: токен той же секунды, что и блокировка, не «раньше» отметки — его закрывает отметка по sid.
        _factory.IdentityAdmin.AddSession(TestUser.Customer, "s-same-second", "sandbox-bff");
        string token = _factory.CreateToken(TestUser.Customer, sessionId: "s-same-second");

        await AdminAsync(HttpMethod.Put, $"/auth/admin/users/{TestUser.Customer.Id}/restriction", new { restricted = true });

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(token));
        Assert.True(_factory.Revocations.HasSession("s-same-second"));
    }

    [Fact]
    public async Task RoleChange_OldTokenRejected()
    {
        string oldToken = _factory.CreateToken(TestUser.Customer, issuedAt: _issuedEarlier);

        await AdminAsync(HttpMethod.Put, $"/auth/admin/users/{TestUser.Customer.Id}/role", new { role = "manager" });

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(oldToken));
    }

    [Fact]
    public async Task LogoutEverywhere_OldTokenRejected()
    {
        string oldToken = _factory.CreateToken(TestUser.Customer, issuedAt: _issuedEarlier);

        await AdminAsync(HttpMethod.Post, $"/auth/admin/users/{TestUser.Customer.Id}/logout", body: null);

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(oldToken));
    }

    [Fact]
    public async Task TerminatedSession_ItsTokensRejected_OtherSessionWorks()
    {
        _factory.IdentityAdmin.AddSession(TestUser.Customer, "s-laptop", "sandbox-bff");
        string laptop = _factory.CreateToken(TestUser.Customer, sessionId: "s-laptop");
        string phone = _factory.CreateToken(TestUser.Customer, sessionId: "s-phone");

        using HttpClient client = _factory.CreateClientWithToken(phone);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(new Uri("/security/sessions/s-laptop/terminate", UriKind.Relative), null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(laptop));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(phone));
    }

    [Fact]
    public async Task ClosedApp_ItsTokensRejected_OtherAppsWork()
    {
        string demo = _factory.CreateToken(TestUser.Customer, issuedAt: _issuedEarlier, authorizedParty: "sandbox-bff");
        string cabinet = _factory.CreateToken(TestUser.Customer, issuedAt: _issuedEarlier, authorizedParty: "account-console");

        await AdminAsync(HttpMethod.Put, $"/auth/admin/users/{TestUser.Customer.Id}/apps/sandbox-bff/access", new { hasAccess = false });

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(demo));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(cabinet));
    }

    [Fact]
    public async Task OwnLogout_RevokesCurrentSession()
    {
        string token = _factory.CreateToken(TestUser.Customer, sessionId: "s-own");
        using HttpClient client = _factory.CreateClientWithToken(token);

        await client.PostAsync(new Uri("/auth/logout", UriKind.Relative), null);

        Assert.True(_factory.Revocations.HasSession("s-own"));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(token));
    }

    [Fact]
    public async Task BackchannelLogout_ValidToken_RevokesSession()
    {
        _factory.IdentityProvider.LogoutNoticeResult = new LogoutNotice(TestUser.Customer.Id.ToString(), "s-keycloak");
        string token = _factory.CreateToken(TestUser.Customer, sessionId: "s-keycloak");

        HttpResponseMessage response = await BackchannelAsync("signed-logout-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(token));
    }

    [Fact]
    public async Task BackchannelLogout_WithoutSid_RevokesUser()
    {
        _factory.IdentityProvider.LogoutNoticeResult = new LogoutNotice(TestUser.Customer.Id.ToString(), null);

        await BackchannelAsync("signed-logout-token");

        Assert.True(_factory.Revocations.HasUser(TestUser.Customer.Id));
    }

    [Fact]
    public async Task BackchannelLogout_InvalidToken_BadRequest_NothingRevoked()
    {
        HttpResponseMessage response = await BackchannelAsync("forged");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(_factory.Revocations.HasUser(TestUser.Customer.Id));
    }

    [Fact]
    public async Task StoreUnavailable_TokenAccepted()
    {
        _factory.Revocations.Broken = true;

        Assert.Equal(HttpStatusCode.OK, await MeAsync(_factory.CreateToken(TestUser.Customer)));
    }

    private async Task<HttpStatusCode> MeAsync(string token)
    {
        using HttpClient client = _factory.CreateClientWithToken(token);
        return (await client.GetAsync(new Uri("/me/", UriKind.Relative))).StatusCode;
    }

    private async Task AdminAsync(HttpMethod method, string path, object? body)
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.AdminInAdminMode);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> BackchannelAsync(string logoutToken)
    {
        using HttpClient client = _factory.CreateClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal) { ["logout_token"] = logoutToken });
        return await client.PostAsync(new Uri("/auth/backchannel-logout", UriKind.Relative), form);
    }
}
