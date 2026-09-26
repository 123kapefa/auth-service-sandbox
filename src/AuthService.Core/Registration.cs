using AuthService.Core.Features.Auth.Cookies;
using AuthService.Core.Features.Auth.ReturnUrls;
using Framework.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.Core;

/// <summary>Регистрация слоя Core: эндпоинты сценариев (IEndpoint) и службы входа из этой сборки.</summary>
public static class Registration
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEndpoints(typeof(Registration).Assembly);

        services.Configure<AuthCookieOptions>(configuration.GetSection(AuthCookieOptions.SECTION_NAME));
        services.Configure<ReturnUrlOptions>(configuration.GetSection(ReturnUrlOptions.SECTION_NAME));
        services.AddDataProtection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuthCookieWriter>();
        services.AddSingleton<LoginStateStore>();
        services.AddSingleton<ReturnUrlValidator>();

        return services;
    }
}
