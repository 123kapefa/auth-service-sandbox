using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AuthService.IntegrationTests;

public sealed class HealthEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    // Порт 1 никто не слушает: база гарантированно недоступна, тест не зависит от Docker.
    private const string UNREACHABLE_DATABASE = "Host=127.0.0.1;Port=1;Database=auth_service;Username=test;Password=test;Timeout=1";

    private readonly WebApplicationFactory<Program> _factory = factory.WithWebHostBuilder(builder =>
        builder.UseSetting("ConnectionStrings:AuthDb", UNREACHABLE_DATABASE));

    [Fact]
    public async Task Live_Returns200_EvenWhenDatabaseIsDown()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_Returns503_WhenDatabaseIsDown()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
