using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Коды проекта: настройки и расход за сегодня — только админ проекта; плагин Keycloak подменён.</summary>
public sealed class CodeSettingsTests : IClassFixture<AuthServiceWebFactory>
{
    private const string SETTINGS = "/auth/admin/codes/settings";
    private const string USAGE = "/auth/admin/codes/usage";

    private readonly AuthServiceWebFactory _factory;

    public CodeSettingsTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        factory.Codes.Reset();
        factory.AuditLog.Reset();
    }

    private static object Request(
        string[]? phoneChannels = null,
        string[]? countries = null,
        long dailyBudget = 100_000,
        int targetPerHour = 5,
        int targetPerDay = 10) =>
        new
        {
            phoneChannels = phoneChannels ?? ["call", "sms"],
            countries = countries ?? ["7"],
            dailyBudget,
            targetPerHour,
            targetPerDay,
            ipPerHour = 10,
            rangePerHour = 20,
            captchaAfter = 2,
        };

    [Fact]
    public async Task Get_SettingsAndServerSettings()
    {
        JsonElement settings = await ResultAsync(await SendAsync(HttpMethod.Get, SETTINGS));

        Assert.Equal("call", settings.GetProperty("phoneChannels")[0].GetString());
        Assert.Equal("7", settings.GetProperty("countries")[0].GetString());
        Assert.Equal(100_000, settings.GetProperty("dailyBudget").GetInt64());
        Assert.Equal(400, settings.GetProperty("smsCost").GetInt64());
        Assert.Equal(7, settings.GetProperty("rangeDigits").GetInt32());
        Assert.Equal("Europe/Moscow", settings.GetProperty("zone").GetString());
    }

    [Fact]
    public async Task Set_Saved_CallFirst_Audited_OnlyChanges()
    {
        JsonElement settings = await ResultAsync(await SendAsync(HttpMethod.Put, SETTINGS,
            Request(phoneChannels: ["sms"], countries: ["7", "375"], dailyBudget: 300_000)));

        Assert.Equal(["sms"], _factory.Codes.Current.PhoneChannels);
        Assert.Equal(["7", "375"], _factory.Codes.Current.Countries);
        Assert.Equal(300_000, settings.GetProperty("dailyBudget").GetInt64());
        AuditEntry entry = Assert.Single(_factory.AuditLog.Entries);
        Assert.Equal((AuditActions.PROJECT_CODES_CHANGED, AuditActions.PROJECT_OBJECT), (entry.Action, entry.ObjectType));
        using JsonDocument changes = JsonDocument.Parse(entry.Changes);
        Assert.Equal(["countries", "dailyBudget", "phoneChannels"], changes.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(100_000, changes.RootElement.GetProperty("dailyBudget").GetProperty("old").GetInt64());

        await ResultAsync(await SendAsync(HttpMethod.Put, SETTINGS, Request(phoneChannels: ["sms", "call"])));
        Assert.Equal(["call", "sms"], _factory.Codes.Current.PhoneChannels);
    }

    [Fact]
    public async Task Set_Unchanged_NotSaved_NotAudited()
    {
        await ResultAsync(await SendAsync(HttpMethod.Put, SETTINGS, Request()));

        Assert.Equal(0, _factory.Codes.Saves);
        Assert.Empty(_factory.AuditLog.Entries);
    }

    public static TheoryData<string, string, string> Invalid => new()
    {
        { "phoneChannels", """{ "phoneChannels": [], "countries": ["7"], "dailyBudget": 0, "targetPerHour": 5, "targetPerDay": 10, "ipPerHour": 10, "rangePerHour": 20, "captchaAfter": 2 }""", "value.is.required" },
        { "phoneChannels", """{ "phoneChannels": ["email"], "countries": ["7"], "dailyBudget": 0, "targetPerHour": 5, "targetPerDay": 10, "ipPerHour": 10, "rangePerHour": 20, "captchaAfter": 2 }""", "value.is.invalid" },
        { "phoneChannels", """{ "phoneChannels": ["sms", "sms"], "countries": ["7"], "dailyBudget": 0, "targetPerHour": 5, "targetPerDay": 10, "ipPerHour": 10, "rangePerHour": 20, "captchaAfter": 2 }""", "codes.duplicates" },
        { "countries", """{ "phoneChannels": ["sms"], "countries": [], "dailyBudget": 0, "targetPerHour": 5, "targetPerDay": 10, "ipPerHour": 10, "rangePerHour": 20, "captchaAfter": 2 }""", "value.is.required" },
        { "countries", """{ "phoneChannels": ["sms"], "countries": ["+7"], "dailyBudget": 0, "targetPerHour": 5, "targetPerDay": 10, "ipPerHour": 10, "rangePerHour": 20, "captchaAfter": 2 }""", "invalid.format" },
        { "dailyBudget", """{ "phoneChannels": ["sms"], "countries": ["7"], "dailyBudget": -1, "targetPerHour": 5, "targetPerDay": 10, "ipPerHour": 10, "rangePerHour": 20, "captchaAfter": 2 }""", "value.out.of.range" },
        { "targetPerHour", """{ "phoneChannels": ["sms"], "countries": ["7"], "dailyBudget": 0, "targetPerHour": 0, "targetPerDay": 10, "ipPerHour": 10, "rangePerHour": 20, "captchaAfter": 2 }""", "value.out.of.range" },
        { "targetPerDay", """{ "phoneChannels": ["sms"], "countries": ["7"], "dailyBudget": 0, "targetPerHour": 5, "targetPerDay": 3, "ipPerHour": 10, "rangePerHour": 20, "captchaAfter": 2 }""", "codes.day_below_hour" },
        { "ipPerHour", """{ "phoneChannels": ["sms"], "countries": ["7"], "dailyBudget": 0, "targetPerHour": 5, "targetPerDay": 10, "rangePerHour": 20, "captchaAfter": 2 }""", "value.out.of.range" },
        { "captchaAfter", """{ "phoneChannels": ["sms"], "countries": ["7"], "dailyBudget": 0, "targetPerHour": 5, "targetPerDay": 10, "ipPerHour": 10, "rangePerHour": 20, "captchaAfter": 101 }""", "value.out.of.range" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Set_Invalid_BadRequest_NothingSaved(string field, string body, string code)
    {
        using HttpClient client = _factory.CreateClientFor(TestUser.AdminInAdminMode);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PutAsync(new Uri(SETTINGS, UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement message = error.RootElement.GetProperty("error").GetProperty("messages")[0];
        Assert.Equal(code, message.GetProperty("code").GetString());
        Assert.Equal(field, message.GetProperty("invalidField").GetString());
        Assert.Equal(0, _factory.Codes.Saves);
    }

    [Fact]
    public async Task Set_KeycloakRefused_Error_NotAudited()
    {
        _factory.Codes.Failure = CodeSettingsErrors.NotPermitted();

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, SETTINGS, Request(dailyBudget: 0));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("codes.not_permitted", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(_factory.AuditLog.Entries);
    }

    [Fact]
    public async Task Usage_Today()
    {
        _factory.Codes.Usage = new ProjectCodeUsage(
            new DateOnly(2026, 10, 7), "Europe/Moscow", 1500, 300_000, 80, Exhausted: false,
            new CodeChannelCount(12, 1200), new CodeChannelCount(1, 400), new CodeChannelCount(3, 0), 16, 14, 88);

        JsonElement usage = await ResultAsync(await SendAsync(HttpMethod.Get, USAGE));

        Assert.Equal("2026-10-07", usage.GetProperty("date").GetString());
        Assert.Equal(1500, usage.GetProperty("budget").GetProperty("spent").GetInt64());
        Assert.Equal(300_000, usage.GetProperty("budget").GetProperty("limit").GetInt64());
        Assert.Equal(12, usage.GetProperty("call").GetProperty("sent").GetInt64());
        Assert.Equal(400, usage.GetProperty("sms").GetProperty("cost").GetInt64());
        Assert.Equal(88, usage.GetProperty("verifiedPercent").GetInt32());
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

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, SETTINGS, caller: caller)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, USAGE, caller: caller)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put, SETTINGS, Request(dailyBudget: 0), caller)).StatusCode);
        Assert.Equal(0, _factory.Codes.Saves);
    }

    [Fact]
    public async Task Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri(USAGE, UriKind.Relative))).StatusCode);
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
