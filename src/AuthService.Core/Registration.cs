using AuthService.Core.Features.Auth.Cookies;
using AuthService.Core.Features.Auth.ReturnUrls;
using AuthService.Core.Features.Auth.UseCases.Callback;
using AuthService.Core.Features.Auth.UseCases.Login;
using AuthService.Core.Features.Auth.UseCases.Logout;
using AuthService.Core.Features.Auth.UseCases.Refresh;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.Core;

/// <summary>Регистрация слоя Core: эндпоинты (IEndpoint), валидаторы, хендлеры сценариев и службы входа.</summary>
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

        services.AddValidatorsFromAssembly(typeof(Registration).Assembly, includeInternalTypes: true);
        services.AddScoped<LoginHandler>();
        services.AddScoped<CallbackHandler>();
        services.AddScoped<RefreshHandler>();
        services.AddScoped<LogoutHandler>();

        return services;
    }
}
