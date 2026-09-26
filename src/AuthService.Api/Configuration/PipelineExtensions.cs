using Framework.Endpoints;

namespace AuthService.Api.Configuration;

/// <summary>Порядок middleware и маршрутов сервиса.</summary>
public static class PipelineExtensions
{
    public static WebApplication UseAuthServicePipeline(this WebApplication app)
    {
        // Первым: всё, что дальше читает IP клиента или схему запроса, должно видеть настоящие значения.
        app.UseAuthServiceForwardedHeaders();

        // После прокси-заголовков (нужны настоящие схема и IP), до эндпоинтов.
        app.UseAuthServiceAuthentication();

        app.MapAuthServiceHealthEndpoints();
        app.MapEndpoints();

        return app;
    }
}
