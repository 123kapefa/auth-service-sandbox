using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Domain.AuditLog;
using AuthService.IntegrationTests.Infrastructure;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Ключи сервисов проекта: выдать, перевыпустить, отозвать — только админ проекта; Keycloak подменён.</summary>
public sealed class ServiceKeysTests : IClassFixture<AuthServiceWebFactory>
{
    private readonly AuthServiceWebFactory _factory;

    public ServiceKeysTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        factory.ServiceRegistry.Reset();
        factory.ServiceKeys.Reset();
        factory.Revocations.Reset();
        factory.AuditLog.Reset();
    }

    [Fact]
    public async Task Issue_RegistersClient_SecretShownOnce_Audited()
    {
        JsonElement issued = await ResultAsync(await IssueAsync("orders", "Заказы"));

        Assert.Equal("service-orders", issued.GetProperty("service").GetProperty("clientId").GetString());
        Assert.Equal(_factory.ServiceRegistry.SecretOf("service-orders"), issued.GetProperty("clientSecret").GetString());
        Assert.Equal(AuditActions.SERVICE_KEY_ISSUED, Assert.Single(_factory.AuditLog.Entries).Action);

        JsonElement list = await ResultAsync(await SendAsync(HttpMethod.Get, "/auth/admin/services"));
        JsonElement service = Assert.Single(list.EnumerateArray());
        Assert.Equal("Заказы", service.GetProperty("name").GetString());
        Assert.False(service.TryGetProperty("clientSecret", out _)); // в списке секрета нет
    }

    [Fact]
    public async Task Issue_RegistrationTokenStoredEncrypted()
    {
        await IssueAsync("orders", "Заказы");

        string stored = Assert.Single(_factory.ServiceKeys.Keys).ProtectedRegistrationToken;
        Assert.DoesNotContain("rat-", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_SameCode_Conflict() =>
        await AssertErrorAsync(
            await IssueAsync("orders", "Заказы"),
            await IssueAsync("orders", "Заказы 2"),
            HttpStatusCode.Conflict,
            "services.exists");

    [Theory]
    [InlineData("Orders")]
    [InlineData("1orders")]
    [InlineData("orders-")]
    [InlineData("заказы")]
    public async Task Issue_InvalidCode_BadRequest(string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await IssueAsync(code, "Заказы")).StatusCode);
        Assert.Empty(_factory.ServiceRegistry.ClientIds);
    }

    [Fact]
    public async Task Issue_NoRegistrationPermit_Conflict()
    {
        _factory.ServiceRegistry.Configured = false;

        await AssertErrorAsync(null, await IssueAsync("orders", "Заказы"), HttpStatusCode.Conflict, "services.registration_unavailable");
    }

    [Fact]
    public async Task Rotate_NewSecret_OldTokensRevoked_Audited()
    {
        string first = (await ResultAsync(await IssueAsync("orders", "Заказы"))).GetProperty("clientSecret").GetString()!;

        JsonElement rotated = await ResultAsync(await SendAsync(HttpMethod.Post, "/auth/admin/services/orders/rotate"));

        string second = rotated.GetProperty("clientSecret").GetString()!;
        Assert.NotEqual(first, second);
        Assert.Equal(_factory.ServiceRegistry.SecretOf("service-orders"), second);
        Assert.True(_factory.Revocations.HasUser(_factory.ServiceRegistry.ServiceAccountOf("service-orders")));
        Assert.Equal(AuditActions.SERVICE_KEY_ROTATED, _factory.AuditLog.Entries[^1].Action);

        // Токен управления сменился при перевыпуске — следующий перевыпуск идёт уже новым.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, "/auth/admin/services/orders/rotate")).StatusCode);
    }

    [Fact]
    public async Task Revoke_ClientDeleted_TokensRevoked_Audited()
    {
        await IssueAsync("orders", "Заказы");
        Guid serviceAccount = _factory.ServiceRegistry.ServiceAccountOf("service-orders");

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Delete, "/auth/admin/services/orders")).StatusCode);

        Assert.Empty(_factory.ServiceRegistry.ClientIds);
        Assert.Empty(_factory.ServiceKeys.Keys);
        Assert.True(_factory.Revocations.HasUser(serviceAccount));
        Assert.Equal(AuditActions.SERVICE_KEY_REVOKED, _factory.AuditLog.Entries[^1].Action);
    }

    [Theory]
    [InlineData("POST", "/auth/admin/services/unknown/rotate")]
    [InlineData("DELETE", "/auth/admin/services/unknown")]
    public async Task UnknownService_NotFound(string method, string path) =>
        await AssertErrorAsync(null, await SendAsync(new HttpMethod(method), path), HttpStatusCode.NotFound, "services.not_found");

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

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/auth/admin/services", caller)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await IssueAsync("orders", "Заказы", caller)).StatusCode);
        Assert.Empty(_factory.ServiceRegistry.ClientIds);
    }

    [Fact]
    public async Task Anonymous_Unauthorized()
    {
        using HttpClient client = _factory.CreateClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/auth/admin/services", UriKind.Relative))).StatusCode);
    }

    private async Task<HttpResponseMessage> IssueAsync(string code, string name, TestUser? caller = null)
    {
        using HttpClient client = _factory.CreateClientFor(caller ?? TestUser.AdminInAdminMode);
        return await client.PostAsJsonAsync(new Uri("/auth/admin/services", UriKind.Relative), new { code, name });
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, TestUser? caller = null)
    {
        using HttpClient client = _factory.CreateClientFor(caller ?? TestUser.AdminInAdminMode);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("result").Clone();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage? first, HttpResponseMessage response, HttpStatusCode status, string code)
    {
        if (first is not null)
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        Assert.Equal(status, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString());
    }
}
