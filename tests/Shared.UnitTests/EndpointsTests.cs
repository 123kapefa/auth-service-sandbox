using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Shared.UnitTests;

public sealed class EndpointsTests
{
    [Fact]
    public void AddEndpoints_FindsImplementationsOnce()
    {
        var services = new ServiceCollection();

        services.AddEndpoints(typeof(PingEndpoint).Assembly);
        services.AddEndpoints(typeof(PingEndpoint).Assembly);

        Assert.Single(services, descriptor => descriptor.ImplementationType == typeof(PingEndpoint));
    }

    [Fact]
    public async Task MapEndpoints_PublishesRoutes()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddEndpoints(typeof(PingEndpoint).Assembly);
        await using WebApplication app = builder.Build();

        app.MapEndpoints();

        IEnumerable<string?> routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText);
        Assert.Contains("/ping", routes);
    }

    public sealed class PingEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app) => app.MapGet("/ping", () => Results.Ok());
    }
}
