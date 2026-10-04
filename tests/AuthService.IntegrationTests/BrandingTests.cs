using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Оформление проекта: прочитать и сохранить — только админ проекта; плагин Keycloak подменён.</summary>
public sealed class BrandingTests : IClassFixture<AuthServiceWebFactory>
{
    private const string PATH = "/auth/admin/branding";
    private const string PNG = "data:image/png;base64,iVBORw0KGgo=";

    private readonly AuthServiceWebFactory _factory;

    public BrandingTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        factory.Branding.Reset();
        factory.AuditLog.Reset();
    }

    [Fact]
    public async Task Get_CurrentBranding()
    {
        _factory.Branding.Current = new ProjectBranding("Магазин", PNG, "#1f6feb", "dark");

        JsonElement branding = await ResultAsync(await SendAsync(HttpMethod.Get));

        Assert.Equal("Магазин", branding.GetProperty("name").GetString());
        Assert.Equal(PNG, branding.GetProperty("logo").GetString());
        Assert.Equal("#1f6feb", branding.GetProperty("color").GetString());
        Assert.Equal("dark", branding.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task Set_Saved_ColorLowercase_NameTrimmed_Audited_WithoutPicture()
    {
        JsonElement branding = await ResultAsync(await SendAsync(HttpMethod.Put, new { name = "  Магазин ", logo = PNG, color = "#1F6FEB", theme = "light" }));

        Assert.Equal(new ProjectBranding("Магазин", PNG, "#1f6feb", "light"), _factory.Branding.Current);
        Assert.Equal("#1f6feb", branding.GetProperty("color").GetString());
        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal((AuditActions.PROJECT_BRANDING_CHANGED, AuditActions.PROJECT_OBJECT, "Магазин"), (entry.Action, entry.ObjectType, entry.ObjectName));
        using JsonDocument changes = JsonDocument.Parse(entry.Changes);
        Assert.Equal("Auth Sandbox", changes.RootElement.GetProperty("name").GetProperty("old").GetString());
        Assert.True(changes.RootElement.GetProperty("logo").GetProperty("new").GetBoolean());
        Assert.DoesNotContain("base64", entry.Changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_ColorDark_Lowercase_Audited()
    {
        JsonElement branding = await ResultAsync(await SendAsync(HttpMethod.Put, new { name = "Auth Sandbox", color = "#1f6feb", colorDark = "#7AA7FF", theme = "system" }));

        Assert.Equal(new ProjectBranding("Auth Sandbox", null, "#1f6feb", "system", "#7aa7ff"), _factory.Branding.Current);
        Assert.Equal("#7aa7ff", branding.GetProperty("colorDark").GetString());
        using JsonDocument changes = JsonDocument.Parse(Assert.Single(_factory.AuditLog.Entries).Changes);
        Assert.Equal("#7aa7ff", changes.RootElement.GetProperty("colorDark").GetProperty("new").GetString());
    }

    [Fact]
    public async Task Set_NullLogoAndColor_Removed()
    {
        _factory.Branding.Current = new ProjectBranding("Магазин", PNG, "#1f6feb", "dark");

        await ResultAsync(await SendAsync(HttpMethod.Put, new { name = "Магазин", logo = (string?)null, color = (string?)null, theme = "system" }));

        Assert.Equal(new ProjectBranding("Магазин", null, null, "system"), _factory.Branding.Current);
    }

    [Fact]
    public async Task Set_Unchanged_NotSaved_NotAudited()
    {
        await ResultAsync(await SendAsync(HttpMethod.Put, new { name = "Auth Sandbox", theme = "system" }));

        Assert.Equal(0, _factory.Branding.Saves);
        Assert.Empty(_factory.AuditLog.Entries);
    }

    public static TheoryData<string, string, string> Invalid => new()
    {
        { "name", """{ "name": "  ", "theme": "system" }""", "value.is.required" },
        { "name", $$"""{ "name": "{{new string('x', 61)}}", "theme": "system" }""", "length.is.invalid" },
        { "color", """{ "name": "X", "color": "red", "theme": "system" }""", "invalid.format" },
        { "color", """{ "name": "X", "color": "#fff", "theme": "system" }""", "invalid.format" },
        { "colorDark", """{ "name": "X", "color": "#1f6feb", "colorDark": "#fff", "theme": "system" }""", "invalid.format" },
        { "colorDark", """{ "name": "X", "colorDark": "#7aa7ff", "theme": "system" }""", "branding.color_dark_without_color" },
        { "theme", """{ "name": "X", "theme": "blue" }""", "value.is.invalid" },
        { "theme", """{ "name": "X" }""", "value.is.invalid" },
        { "logo", """{ "name": "X", "logo": "https://evil.example/logo.png", "theme": "system" }""", "invalid.format" },
        { "logo", """{ "name": "X", "logo": "data:text/html;base64,PHNjcmlwdD4=", "theme": "system" }""", "invalid.format" },
        { "logo", $$"""{ "name": "X", "logo": "data:image/png;base64,{{Convert.ToBase64String(new byte[(100 * 1024) + 1])}}", "theme": "system" }""", "branding.logo_too_large" },
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

    [Fact]
    public async Task Set_LogoExactly100Kb_Accepted()
    {
        string logo = $"data:image/webp;base64,{Convert.ToBase64String(new byte[100 * 1024])}";

        await ResultAsync(await SendAsync(HttpMethod.Put, new { name = "X", logo, theme = "system" }));

        Assert.Equal(logo, _factory.Branding.Current.Logo);
    }

    [Fact]
    public async Task Set_KeycloakRefused_Error_NotAudited()
    {
        _factory.Branding.Failure = BrandingErrors.NotPermitted();

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, new { name = "Магазин", theme = "dark" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("branding.not_permitted", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(_factory.AuditLog.Entries);
    }

    public static TheoryData<string> NotProjectAdmins => new() { "manager", "admin-in-customer-mode", "customer" };

    [Theory]
    [MemberData(nameof(NotProjectAdmins))]
    public async Task NotProjectAdmin_Forbidden(string who)
    {
        TestUser caller = who switch
        {
            "manager" => TestUser.ManagerInManagerMode,
            "admin-in-customer-mode" => TestUser.AdminInCustomerMode,
            _ => TestUser.Customer,
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, caller: caller)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put, new { name = "X", theme = "dark" }, caller)).StatusCode);
        Assert.Equal(0, _factory.Branding.Saves);
    }

    [Fact]
    public async Task Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri(PATH, UriKind.Relative))).StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, object? body = null, TestUser? caller = null)
    {
        using HttpClient client = _factory.CreateClientFor(caller ?? TestUser.AdminInAdminMode);
        using var request = new HttpRequestMessage(method, new Uri(PATH, UriKind.Relative));
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
