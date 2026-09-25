namespace AuthService.Api.Configuration;

/// <summary>Порядок middleware и маршрутов сервиса.</summary>
public static class PipelineExtensions
{
    public static WebApplication UseAuthServicePipeline(this WebApplication app)
    {
        app.MapAuthServiceHealthEndpoints();

        return app;
    }
}
