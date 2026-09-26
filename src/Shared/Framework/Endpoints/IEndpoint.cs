using Microsoft.AspNetCore.Routing;

namespace Framework.Endpoints;

/// <summary>Эндпоинт, который регистрирует себя сам: лежит рядом со своим сценарием, а не в Program.cs.</summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
