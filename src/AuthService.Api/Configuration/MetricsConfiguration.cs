using AuthService.Core;
using PlatformAuth;
using Prometheus;

namespace AuthService.Api.Configuration;

/// <summary>
/// Метрики для Prometheus: /metrics на отдельном порту (Metrics:Port, по умолчанию 9100), а не на порту сервиса. Порт метрик
/// не публикуется наружу (ни Ingress, ни прокси, ни порт хоста): счётчики входов и отказов не для чужих глаз; Prometheus
/// ходит к нему по внутреннему адресу. Metrics:Port=0 — метрики выключены. Отдаются: метрики сервиса (auth, в том числе запросы к Keycloak),
/// проверки токенов и отзыва (platformauth), встроенные метрики ASP.NET Core, Kestrel, Npgsql и среды выполнения.
/// </summary>
public static class MetricsConfiguration
{
    public const string SECTION_NAME = "Metrics";

    // Свои счётчики переносит PrometheusMeterBridge (настоящие counter с _total); адаптер prometheus-net публикует счётчики как gauge.
    private static readonly string[] _ownMeters = ["auth", "platformauth"];

    // Встроенные источники .NET, которые берёт адаптер prometheus-net: гистограммы и gauge без таких проблем.
    private static readonly HashSet<string> _meters = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Server.Kestrel",
        "Npgsql",
    };

    public static IServiceCollection AddAuthServiceMetrics(this IServiceCollection services, IConfiguration configuration)
    {
        int port = configuration.GetValue($"{SECTION_NAME}:Port", 9100);
        if (port is < 0 or > 65535)
        {
            throw new InvalidOperationException($"{SECTION_NAME}:Port — порт 1–65535 или 0 (метрики выключены), получено {port}.");
        }

        if (port > 0)
        {
            // Только нужные встроенные источники .NET: ASP.NET Core, Kestrel, Npgsql (ответы Keycloak считает сам сервис: auth_keycloak_*).
            // Без фильтра в Prometheus попали бы сотни внутренних счётчиков (QUIC, EF Core, пул памяти).
            Metrics.ConfigureMeterAdapter(options =>
            {
                options.InstrumentFilterPredicate = instrument => _meters.Contains(instrument.Meter.Name);
            });
            services.AddMetricServer(options => options.Port = (ushort)port);
            services.AddSingleton<PrometheusMeterBridge>(provider => new PrometheusMeterBridge(
                _ownMeters,
                provider.GetRequiredService<ILogger<PrometheusMeterBridge>>(),
                seed: () =>
                {
                    AuthMetrics.Seed();
                    PlatformAuthMetrics.Seed();
                }));
            services.AddHostedService(provider => provider.GetRequiredService<PrometheusMeterBridge>());
        }

        return services;
    }
}
