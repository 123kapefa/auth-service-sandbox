using Framework.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.Core;

/// <summary>Регистрация слоя Core: эндпоинты сценариев (IEndpoint) из этой сборки.</summary>
public static class Registration
{
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        services.AddEndpoints(typeof(Registration).Assembly);

        return services;
    }
}
