using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Способы входа проекта: прочитать, поменять, сколько людей затронет — только админ проекта; плагин подменён.</summary>
public sealed class ProjectSignInTests : IClassFixture<AuthServiceWebFactory>
{
    private const string PATH = "/auth/admin/sign-in/methods";

    private readonly AuthServiceWebFactory _factory;

    public ProjectSignInTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        factory.SignIn.Reset();
        factory.AuditLog.Reset();
    }

    private static string InDays(int days) =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Все способы включены, кроме заданных.
    private static object Request(string twoFactor = "optional", params (string Method, object State)[] changed)
    {
        var methods = ProjectSignInRules.Methods.ToDictionary(method => method, _ => (object)new { status = "on" }, StringComparer.Ordinal);
        foreach ((string method, object state) in changed)
        {
            methods[method] = state;
        }

        return new { methods, twoFactor };
    }

    [Fact]
    public async Task Get_AllMethods()
    {
        JsonElement result = await ResultAsync(await SendAsync(HttpMethod.Get, PATH));

        Assert.Equal("on", result.GetProperty("methods").GetProperty("phone").GetProperty("status").GetString());
        Assert.True(result.GetProperty("methods").GetProperty("yandex").GetProperty("configured").GetBoolean());
        Assert.Equal("optional", result.GetProperty("twoFactor").GetString());
    }

    [Fact]
    public async Task Set_TransitionAndTwoFactor_Saved_Audited()
    {
        string until = InDays(30);
        JsonElement result = await ResultAsync(await SendAsync(HttpMethod.Put, PATH,
            Request("required", ("phone", new { status = "transition", until }), ("password", new { status = "off" }))));

        JsonElement phone = result.GetProperty("methods").GetProperty("phone");
        Assert.Equal(("transition", until), (phone.GetProperty("status").GetString(), phone.GetProperty("until").GetString()));
        Assert.Equal(FakeProjectSignIn.Now, phone.GetProperty("since").GetDateTimeOffset());
        Assert.Equal("required", result.GetProperty("twoFactor").GetString());
        Assert.True(_factory.SignIn.Current.TwoFactorRequired);

        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal(AuditActions.PROJECT_SIGN_IN_CHANGED, entry.Action);
        using JsonDocument changes = JsonDocument.Parse(entry.Changes);
        Assert.Equal(["password", "phone", "twoFactor"], changes.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal($"transition до {until}", changes.RootElement.GetProperty("phone").GetProperty("new").GetString());
    }

    [Fact]
    public async Task Set_Unchanged_NotSaved_NotAudited()
    {
        await ResultAsync(await SendAsync(HttpMethod.Put, PATH, Request()));

        Assert.Equal(0, _factory.SignIn.Saves);
        Assert.Empty(_factory.AuditLog.Entries);
    }

    public static TheoryData<string, string> Invalid => new()
    {
        { "noRegistration", "sign_in.no_registration" },
        { "passkeyTransition", "sign_in.transition_not_allowed" },
        { "pastDate", "sign_in.transition_date" },
        { "farDate", "sign_in.transition_date" },
        { "missingMethod", "value.is.required" },
        { "badStatus", "value.is.invalid" },
        { "badTwoFactor", "value.is.invalid" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Set_Invalid_BadRequest_NothingSaved(string kind, string code)
    {
        object body = kind switch
        {
            "noRegistration" => Request("optional", ("phone", new { status = "off" }), ("email", new { status = "transition", until = InDays(10) }),
                ("yandex", new { status = "off" }), ("esia", new { status = "off" })),
            "passkeyTransition" => Request("optional", ("passkey", new { status = "transition", until = InDays(10) })),
            "pastDate" => Request("optional", ("password", new { status = "transition", until = InDays(0) })),
            "farDate" => Request("optional", ("password", new { status = "transition", until = InDays(400) })),
            "missingMethod" => new { methods = new { phone = new { status = "on" } }, twoFactor = "optional" },
            "badStatus" => Request("optional", ("email", new { status = "maybe" })),
            _ => Request("sometimes"),
        };

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, PATH, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"\"{code}\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, _factory.SignIn.Saves);
    }

    [Fact]
    public async Task Set_PluginRefused_Error_NotAudited()
    {
        _factory.SignIn.Failure = ProjectSignInErrors.Rejected("Яндекс ID не настроен в Keycloak.");

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, PATH, Request("required"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sign_in.invalid", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(_factory.AuditLog.Entries);
    }

    [Fact]
    public async Task Affected_UsersAndOnlyThisWay()
    {
        JsonElement result = await ResultAsync(await SendAsync(HttpMethod.Get, $"{PATH}/phone/affected"));

        Assert.Equal((8410, 1240), (result.GetProperty("users").GetInt64(), result.GetProperty("only").GetInt64()));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"{PATH}/telegram/affected")).StatusCode);
    }

    [Fact]
    public async Task Manager_Forbidden()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, PATH, caller: TestUser.ManagerInManagerMode)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put, PATH, Request(), TestUser.ManagerInManagerMode)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, $"{PATH}/phone/affected", caller: TestUser.AdminInCustomerMode)).StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, TestUser? caller = null)
    {
        using HttpClient client = _factory.CreateClientFor(caller ?? TestUser.AdminInAdminMode);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("result").Clone();
    }
}
