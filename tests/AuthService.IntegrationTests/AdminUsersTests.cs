using System.Net;
using System.Text.Json;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>GET /auth/admin/users и /auth/admin/users/{id} с подставным Admin API.</summary>
public sealed class AdminUsersTests : IClassFixture<AuthServiceWebFactory>
{
    private readonly AuthServiceWebFactory _factory;

    public AdminUsersTests(AuthServiceWebFactory factory)
    {
        _factory = factory;
        _factory.IdentityAdmin.Reset();
        // В «Keycloak»: покупатель и два сотрудника (customer у всех по умолчанию — в ролях клиента его нет).
        _factory.IdentityAdmin.Add(TestUser.Customer, []);
        _factory.IdentityAdmin.Add(TestUser.AdminInAdminMode, [AuthModes.ADMIN]);
        _factory.IdentityAdmin.Add(TestUser.ManagerInManagerMode, [AuthModes.MANAGER], enabled: false, hasPassword: false);
    }

    [Fact]
    public async Task Admin_SeesEveryone_WithRoles()
    {
        JsonElement page = await GetResultAsync(TestUser.AdminInAdminMode, "/auth/admin/users");

        Assert.Equal(
            ["70987654321:manager", "71234567890:admin", "79990000001:customer"],
            Rows(page).Select(row => $"{row.GetProperty("username").GetString()}:{row.GetProperty("role").GetString()}").Order(StringComparer.Ordinal));
        Assert.False(page.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Manager_SeesOnlyCustomers()
    {
        JsonElement page = await GetResultAsync(TestUser.ManagerInManagerMode, "/auth/admin/users");

        JsonElement row = Assert.Single(Rows(page));
        Assert.Equal("79990000001", row.GetProperty("username").GetString());
        Assert.Equal("customer", row.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Paging_AsksOneMore_ForHasMore()
    {
        JsonElement page = await GetResultAsync(TestUser.AdminInAdminMode, "/auth/admin/users?page=2&pageSize=1");

        Assert.Equal((null, 1, 2), Assert.Single(_factory.IdentityAdmin.Searches));
        Assert.Single(Rows(page));
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal(2, page.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task PhoneSearch_IsDigits_LikeTheLogin()
    {
        JsonElement page = await GetResultAsync(TestUser.AdminInAdminMode, "/auth/admin/users?search=%2B7%20(999)%20000-00-01");

        Assert.Equal("79990000001", Assert.Single(_factory.IdentityAdmin.Searches).Search);
        Assert.Equal("79990000001", Assert.Single(Rows(page)).GetProperty("username").GetString());
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=0")]
    public async Task BadPaging_IsValidationError(string query)
    {
        HttpResponseMessage response = await GetAsync(TestUser.AdminInAdminMode, $"/auth/admin/users?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.IdentityAdmin.Searches);
    }

    [Fact]
    public async Task Detail_Admin_SeesStaff()
    {
        JsonElement user = await GetResultAsync(TestUser.AdminInAdminMode, $"/auth/admin/users/{TestUser.ManagerInManagerMode.Id}");

        Assert.Equal("manager", user.GetProperty("role").GetString());
        Assert.Equal(["customer", "manager"], user.GetProperty("availableRoles").EnumerateArray().Select(role => role.GetString()));
        Assert.True(user.GetProperty("isRestricted").GetBoolean());
        Assert.False(user.GetProperty("hasPassword").GetBoolean());
    }

    [Fact]
    public async Task Detail_Manager_Customer_Ok_Staff_Forbidden()
    {
        HttpResponseMessage customer = await GetAsync(TestUser.ManagerInManagerMode, $"/auth/admin/users/{TestUser.Customer.Id}");
        HttpResponseMessage staff = await GetAsync(TestUser.ManagerInManagerMode, $"/auth/admin/users/{TestUser.AdminInAdminMode.Id}");

        Assert.Equal(HttpStatusCode.OK, customer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);
        Assert.Equal("users.staff_forbidden", await ErrorCodeAsync(staff));
    }

    [Fact]
    public async Task Detail_Unknown_NotFound()
    {
        HttpResponseMessage response = await GetAsync(TestUser.AdminInAdminMode, $"/auth/admin/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("users.not_found", await ErrorCodeAsync(response));
    }

    [Theory]
    [MemberData(nameof(WithoutUsersView))]
    public async Task WithoutUsersView_Forbidden(TestUser user)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(user, "/auth/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(user, $"/auth/admin/users/{TestUser.Customer.Id}")).StatusCode);
    }

    public static TheoryData<TestUser> WithoutUsersView => [TestUser.Customer, TestUser.AdminInCustomerMode];

    private static IEnumerable<JsonElement> Rows(JsonElement page) => page.GetProperty("items").EnumerateArray();

    private async Task<HttpResponseMessage> GetAsync(TestUser user, string path)
    {
        using HttpClient client = _factory.CreateClientFor(user);
        return await client.GetAsync(new Uri(path, UriKind.Relative));
    }

    private async Task<JsonElement> GetResultAsync(TestUser user, string path)
    {
        HttpResponseMessage response = await GetAsync(user, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("result").Clone();
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString();
    }
}
