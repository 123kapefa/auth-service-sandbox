using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>
/// Оформление приложения поверх проекта (только админ проекта) и итоговое оформление для кабинета (/me/branding);
/// плагин Keycloak подменён.
/// </summary>
public sealed class AppBrandingTests : IClassFixture<AuthServiceWebFactory>
{
    private const string APP = FakeIdentityAdmin.MANAGER_1C;
    private const string PATH = $"/auth/admin/apps/{APP}/branding";
    private const string PNG = "data:image/png;base64,iVBORw0KGgo=";

    private readonly AuthServiceWebFactory _factory;

    public AppBrandingTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        factory.IdentityAdmin.Reset();
        factory.Branding.Reset();
        factory.AuditLog.Reset();
    }

    [Fact]
    public async Task Get_OwnAndProject()
    {
        _factory.Branding.Current = new ProjectBranding("Магазин", null, "#1f6feb", "dark");
        _factory.Branding.Apps[APP] = new AppBranding("Менеджер 1С", PNG, null);

        JsonElement branding = await ResultAsync(await SendAsync(HttpMethod.Get, PATH));

        Assert.Equal(APP, branding.GetProperty("clientId").GetString());
        Assert.Equal("Менеджер 1С", branding.GetProperty("name").GetString());
        Assert.Equal(PNG, branding.GetProperty("logo").GetString());
        Assert.Equal(JsonValueKind.Null, branding.GetProperty("color").ValueKind);
        Assert.Equal("#1f6feb", branding.GetProperty("project").GetProperty("color").GetString());
        Assert.Equal("dark", branding.GetProperty("project").GetProperty("theme").GetString());
    }

    [Fact]
    public async Task Set_Saved_Trimmed_Lowercase_AuditedInAppJournal_WithoutPicture()
    {
        JsonElement branding = await ResultAsync(await SendAsync(HttpMethod.Put, PATH, new { name = " Менеджер ", logo = PNG, color = "#AA3300" }));

        Assert.Equal(new AppBranding("Менеджер", PNG, "#aa3300"), _factory.Branding.Apps[APP]);
        Assert.Equal("#aa3300", branding.GetProperty("color").GetString());
        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal((AuditActions.APP_BRANDING_CHANGED, AuditActions.APP_OBJECT, APP, APP), (entry.Action, entry.ObjectType, entry.ObjectName, entry.App));
        Assert.DoesNotContain("base64", entry.Changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_ColorDark_SavedWithColor()
    {
        JsonElement branding = await ResultAsync(await SendAsync(HttpMethod.Put, PATH, new { color = "#aa3300", colorDark = "#FF8866" }));

        Assert.Equal(new AppBranding(null, null, "#aa3300", "#ff8866"), _factory.Branding.Apps[APP]);
        Assert.Equal("#ff8866", branding.GetProperty("colorDark").GetString());
    }

    [Fact]
    public async Task Set_AllNull_AsProject_Unchanged_NotSaved()
    {
        await ResultAsync(await SendAsync(HttpMethod.Put, PATH, new { name = (string?)null, logo = (string?)null, color = (string?)null }));

        Assert.Equal(0, _factory.Branding.Saves);
        Assert.Empty(_factory.AuditLog.Entries);
    }

    public static TheoryData<string, string, string> Invalid => new()
    {
        { "name", """{ "name": "  " }""", "length.is.invalid" },
        { "name", $$"""{ "name": "{{new string('x', 61)}}" }""", "length.is.invalid" },
        { "color", """{ "color": "#fff" }""", "invalid.format" },
        { "colorDark", """{ "color": "#aa3300", "colorDark": "red" }""", "invalid.format" },
        { "colorDark", """{ "colorDark": "#ff8866" }""", "branding.color_dark_without_color" },
        { "logo", """{ "logo": "https://evil.example/logo.png" }""", "invalid.format" },
        { "logo", $$"""{ "logo": "data:image/png;base64,{{Convert.ToBase64String(new byte[(100 * 1024) + 1])}}" }""", "branding.logo_too_large" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Set_Invalid_BadRequest_NothingSaved(string field, string body, string code)
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.AdminInAdminMode);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PutAsync(new Uri(PATH, UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement message = error.RootElement.GetProperty("error").GetProperty("messages")[0];
        Assert.Equal(code, message.GetProperty("code").GetString());
        Assert.Equal(field, message.GetProperty("invalidField").GetString());
        Assert.Equal(0, _factory.Branding.Saves);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("sandbox-bff")] // клиент только для входа, без ролей — не приложение
    public async Task NotAnApp_NotFound(string clientId)
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/auth/admin/apps/{clientId}/branding", new { name = "X" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, _factory.Branding.Saves);
    }

    [Fact]
    public async Task AppAdmin_NotProjectAdmin_Forbidden()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, PATH, caller: TestUser.ManagerInManagerMode)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put, PATH, new { name = "X" }, TestUser.ManagerInManagerMode)).StatusCode);
        Assert.Equal(0, _factory.Branding.Saves);
    }

    [Fact]
    public async Task Account_Effective_ProjectWithAppOnTop_AnyUser()
    {
        _factory.Branding.Current = new ProjectBranding("Магазин", PNG, "#1f6feb", "light", "#7aa7ff");
        _factory.Branding.Apps[APP] = new AppBranding("Менеджер 1С", null, "#aa3300");

        JsonElement project = await ResultAsync(await SendAsync(HttpMethod.Get, "/me/branding", caller: TestUser.Customer));
        JsonElement app = await ResultAsync(await SendAsync(HttpMethod.Get, $"/me/branding?app={APP}", caller: TestUser.Customer));

        Assert.Equal(("Магазин", "#1f6feb", "#7aa7ff"), (
            project.GetProperty("name").GetString(),
            project.GetProperty("color").GetString(),
            project.GetProperty("colorDark").GetString()));
        // Свой акцент приложения — парой: без своего тёмного не берёт тёмный проекта.
        Assert.Equal(JsonValueKind.Null, app.GetProperty("colorDark").ValueKind);
        Assert.Equal(("Менеджер 1С", PNG, "#aa3300", "light"), (
            app.GetProperty("name").GetString(),
            app.GetProperty("logo").GetString(),
            app.GetProperty("color").GetString(),
            app.GetProperty("theme").GetString()));
    }

    [Fact]
    public async Task Account_InvalidApp_ProjectOnly()
    {
        await ResultAsync(await SendAsync(HttpMethod.Get, "/me/branding?app=%2F..%2Fx", caller: TestUser.Customer));

        Assert.Null(_factory.Branding.LastEffectiveApp);
    }

    [Fact]
    public async Task Account_Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/me/branding", UriKind.Relative))).StatusCode);
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
