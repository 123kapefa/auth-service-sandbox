using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuthService.Client.AppCatalog;

public static class AppCatalogExtensions
{
    /// <summary>
    /// Права и встроенные роли приложения — из кода: при старте сервис отправляет их в сервис входа своим ключом
    /// (подробно — <see cref="AppCatalogBuilder"/>). Каталог проверяется сразу, здесь: ошибка в имени или роль с
    /// необъявленным правом — исключение при старте, а не 403 в работе.
    /// </summary>
    /// <param name="services">Сервисы приложения.</param>
    /// <param name="configuration">
    /// Адрес сервиса входа (AuthService:BaseUrl) и ключ (секция ServiceToken): клиент API подключается здесь же, если
    /// ещё не подключён через AddAuthServiceClient. Ключу нужны код приложения (service-{код}) и право
    /// service.catalog.declare.
    /// </param>
    /// <param name="configure">Каталог: Permission(...) и Role(...).</param>
    public static IServiceCollection AddAppCatalog(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AppCatalogBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        AppCatalogBuilder catalog = new();
        configure(catalog);

        if (!services.Any(service => service.ServiceType == typeof(IAuthServiceClient)))
        {
            services.AddAuthServiceClient(configuration);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService(provider => ActivatorUtilities.CreateInstance<AppCatalogPublisher>(provider, catalog.Build()));
        return services;
    }
}
