using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace AuthService.Api.Configuration;

/// <summary>
/// Доверие заголовкам X-Forwarded-For / X-Forwarded-Proto от прокси (nginx), который снимает TLS.
/// Заголовки принимаются только от сетей из ForwardedHeaders:TrustedNetworks: иначе любой клиент
/// мог бы подменить свой IP (обход rate-limit по IP) и схему запроса.
/// </summary>
public static class ForwardedHeadersConfiguration
{
    public const string TRUSTED_NETWORKS_KEY = "ForwardedHeaders:TrustedNetworks";

    // Docker-сети (172.16.0.0/12 покрывает bridge и сети compose) и loopback — как в PlatformOptions образца.
    private static readonly string[] _defaultTrustedNetworks = ["172.16.0.0/12", "127.0.0.0/8"];

    public static IServiceCollection AddAuthServiceForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string[] configured = configuration.GetSection(TRUSTED_NETWORKS_KEY).Get<string[]>() ?? _defaultTrustedNetworks;
        IPNetwork[] trustedNetworks = [.. configured.Select(ParseNetwork)];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (IPNetwork network in trustedNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        return services;
    }

    public static IApplicationBuilder UseAuthServiceForwardedHeaders(this IApplicationBuilder app) =>
        app.UseForwardedHeaders();

    // Ошибка в настройке останавливает старт: молча пропущенная сеть = прокси, которому не доверяют.
    private static IPNetwork ParseNetwork(string value) =>
        IPNetwork.TryParse(value, out IPNetwork network)
            ? network
            : throw new InvalidOperationException($"{TRUSTED_NETWORKS_KEY}: «{value}» — не сеть в формате CIDR (например, 172.16.0.0/12).");
}
