using System.Net;
using System.Text;
using AuthService.Client;
using AuthService.Contracts.Services;
using CSharpFunctionalExtensions;
using SharedKernel;
using Xunit;

namespace AuthService.UnitTests;

// Клиент API сервиса входа: адреса запросов, разбор Envelope — результат и ошибка с кодом сервиса.
public sealed class AuthServiceClientTests
{
    private static readonly Guid _user = Guid.Parse("1e9ac700-0000-4000-8000-000000000001");

    [Fact]
    public async Task FindUser_ParsesResult()
    {
        Stub api = new(HttpStatusCode.OK, $$$"""{"result":{"id":"{{{_user}}}","username":"79990000001","phone":"+79990000001","email":null,"enabled":true,"restrictionReason":null,"createdAt":"2026-10-03T10:00:00+00:00"}}""");

        Result<ServiceUserResponse, Error> user = await Client(api).FindUserAsync("+7 999 000-00-01", default);

        Assert.Equal("+79990000001", user.Value.Phone);
        Assert.Equal("http://auth/auth/services/users?login=%2B7%20999%20000-00-01", api.Url);
    }

    [Fact]
    public async Task CheckAccess_PassesAppAndPermission()
    {
        Stub api = new(HttpStatusCode.OK, """{"result":{"allowed":false,"reason":"permission.missing","activeMode":"customer","permissions":["profile.view"]}}""");

        Result<AccessCheckResponse, Error> access = await Client(api).CheckAccessAsync(_user, "orders.view", "orders", default);

        Assert.False(access.Value.Allowed);
        Assert.Equal("permission.missing", access.Value.Reason);
        Assert.Equal($"http://auth/auth/services/users/{_user}/access?permission=orders.view&app=orders", api.Url);
    }

    [Fact]
    public async Task NotFound_ErrorWithServiceCode()
    {
        Stub api = new(HttpStatusCode.NotFound, """{"result":null,"error":{"messages":[{"code":"users.not_found","message":"Пользователь не найден."}],"type":"NOT_FOUND"}}""");

        Result<ServiceUserResponse, Error> user = await Client(api).GetUserAsync(_user, default);

        Assert.True(user.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, user.Error.Type);
        Assert.Equal("users.not_found", user.Error.Messages[0].Code);
    }

    [Fact]
    public async Task NotJson_TransientFailure_NotException()
    {
        Stub api = new(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>");

        Result<ServiceUserResponse, Error> user = await Client(api).GetUserAsync(_user, default);

        Assert.True(user.IsFailure);
        Assert.Equal("auth_service.bad_response", user.Error.Messages[0].Code);
    }

    [Fact]
    public async Task DeclareCatalog_PutsCatalog()
    {
        Stub api = new(HttpStatusCode.OK, """{"result":{"app":"orders","createdPermissions":["orders.view"],"updatedPermissions":[],"createdRoles":["customer"],"keptRoles":[],"orphans":[]}}""");

        Result<DeclareCatalogResponse, Error> declared = await Client(api).DeclareCatalogAsync(
            new DeclareCatalogRequest([new("orders.view", "Свои заказы")], [new("customer", "Покупатель", ["orders.view"])]), default);

        Assert.Equal("orders", declared.Value.App);
        Assert.Equal(["customer"], declared.Value.CreatedRoles);
        Assert.Equal(HttpMethod.Put, api.Method);
        Assert.Equal("http://auth/auth/services/catalog", api.Url);
        Assert.Contains("\"orders.view\"", api.Body, StringComparison.Ordinal);
    }

    private static AuthServiceClient Client(Stub api) => new(new HttpClient(api) { BaseAddress = new Uri("http://auth/") });

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public HttpMethod? Method { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            Method = request.Method;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
