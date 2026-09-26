using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts.Me;
using AuthService.IntegrationTests.Infrastructure;
using PlatformAuth;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>Критерий этапа 2: по каждому пользователю — какие эндпоинты дают 200, а какие 403 (без токена — 401).</summary>
public sealed class AccessMatrixTests(AuthServiceWebFactory factory) : IClassFixture<AuthServiceWebFactory>
{
    private static readonly string[] _routes =
    [
        "/me/",
        "/demo/admin-only/",
        "/demo/manager-or-admin/",
        "/demo/profile/",
        "/demo/users/",
        "/demo/users/block/",
        "/demo/users/roles/",
        "/demo/audit/",
    ];

    private static readonly string[] _customerRoutes = ["/me/", "/demo/profile/"];

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static TheoryData<TestUser, string[]> AllowedRoutes => new()
    {
        { TestUser.Customer, _customerRoutes },
        { TestUser.AdminInCustomerMode, _customerRoutes },
        { TestUser.ManagerInCustomerMode, _customerRoutes },
        { TestUser.AdminInAdminMode, _routes },
        {
            TestUser.ManagerInManagerMode,
            ["/me/", "/demo/manager-or-admin/", "/demo/profile/", "/demo/users/", "/demo/users/block/"]
        },
    };

    [Theory]
    [MemberData(nameof(AllowedRoutes))]
    public async Task User_GetsOkOnlyWhereAllowed_ElseForbidden(TestUser user, string[] allowed)
    {
        using HttpClient client = factory.CreateClientFor(user);

        foreach (string route in _routes)
        {
            HttpStatusCode expected = allowed.Contains(route) ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            HttpResponseMessage response = await client.GetAsync(new Uri(route, UriKind.Relative));
            Assert.True(expected == response.StatusCode, $"{user}: {route} → {(int)response.StatusCode}, ожидали {(int)expected}");
        }
    }

    [Fact]
    public async Task Anonymous_GetsUnauthorizedEverywhere()
    {
        using HttpClient client = factory.CreateClientFor(null);

        foreach (string route in _routes)
        {
            HttpResponseMessage response = await client.GetAsync(new Uri(route, UriKind.Relative));
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{route} → {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task ForeignSignedToken_IsUnauthorized()
    {
        using var foreign = new AuthServiceWebFactory();
        using HttpClient client = factory.CreateClientFor(null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", foreign.CreateToken(TestUser.AdminInAdminMode));

        HttpResponseMessage response = await client.GetAsync(new Uri("/demo/admin-only/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsTokenDataAndPermissionsOfActiveMode()
    {
        TestUser user = TestUser.ManagerInManagerMode;
        using HttpClient client = factory.CreateClientFor(user);

        JsonElement envelope = await client.GetFromJsonAsync<JsonElement>(new Uri("/me/", UriKind.Relative));

        MeResponse me = envelope.GetProperty("result").Deserialize<MeResponse>(_json)!;
        Assert.False(envelope.GetProperty("isError").GetBoolean());
        Assert.Equal(user.Id, me.Id);
        Assert.Equal(user.Phone, me.Phone);
        Assert.Equal(AuthModes.MANAGER, me.ActiveMode);
        Assert.Equal(["profile.view", "users.block", "users.view"], me.Permissions);
    }
}
