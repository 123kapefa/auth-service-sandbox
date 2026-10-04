using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Endpoints;

public static class EndpointsExtensions
{
    /// <summary>Находит в сборке все реализации IEndpoint и регистрирует их.</summary>
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        IEnumerable<ServiceDescriptor> descriptors = assembly.DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false } && type.IsAssignableTo(typeof(IEndpoint)))
            .Select(type => ServiceDescriptor.Transient(typeof(IEndpoint), type));

        services.TryAddEnumerable(descriptors);

        return services;
    }

    /// <summary>Публикует все зарегистрированные эндпоинты (или внутри группы маршрутов).</summary>
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app, RouteGroupBuilder? group = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        IEndpointRouteBuilder target = group ?? app;
        foreach (IEndpoint endpoint in app.ServiceProvider.GetRequiredService<IEnumerable<IEndpoint>>())
        {
            endpoint.MapEndpoint(target);
        }

        return app;
    }
}
