using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using AuthService.Core;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using PlatformAuth;
using PlatformAuth.Telemetry;

namespace AuthService.Api.Configuration;

/// <summary>
/// 13.4: ограничения частоты запросов к тем адресам сервиса, которые открыты без входа или дороги для Keycloak и базы:
/// <c>/auth/login</c> и <c>/auth/refresh</c> — по IP клиента (за прокси — настоящий IP, см. ForwardedHeaders), <c>/auth/services/*</c> —
/// по ключу сервиса (client_id из токена), а запросы без действующего токена сервиса — по IP, строже.
/// Лимит — скользящее окно: без двойного всплеска на стыке окон. Превышение — 429 с Retry-After и обычным телом ошибки.
/// Пороги — секция <c>RateLimiting</c> (appsettings, переменные <c>RateLimiting__Login__PermitLimit</c> и т. п.); 0 в PermitLimit
/// снимает лимит этой политики, <c>RateLimiting:Enabled=false</c> — все лимиты. Коды на телефон и пароли ограничивает Keycloak и прокси
/// (docs/environments.md), здесь их нет.
/// </summary>
public static class RateLimitingConfiguration
{
    public const string SECTION = "RateLimiting";

    public const string POLICY_LOGIN = "login";
    public const string POLICY_REFRESH = "refresh";
    public const string POLICY_SERVICES = "services";
    public const string POLICY_SERVICES_ANONYMOUS = "services_anonymous";

    private const int SEGMENTS_PER_WINDOW = 6;

    public static IServiceCollection AddAuthServiceRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        RateLimitingOptions options = configuration.GetSection(SECTION).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        options.Validate();

        services.AddSingleton(options);
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteRejectionAsync;
            if (options.Enabled)
            {
                limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => Partition(context, options));
            }
        });

        return services;
    }

    // Лимитер ставится после аутентификации (нужен client_id из токена сервиса), но до авторизации и эндпоинтов.
    public static IApplicationBuilder UseAuthServiceRateLimiting(this IApplicationBuilder app) => app.UseRateLimiter();

    /// <summary>IP клиента как ключ лимита: IPv4-в-IPv6 приводится к IPv4, IPv6 — к сети /64 (иначе один хост занимает 2^64 адресов).</summary>
    public static string ClientKey(HttpContext context)
    {
        IPAddress? address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        byte[] bytes = address.GetAddressBytes();
        return string.Create(CultureInfo.InvariantCulture, $"{Convert.ToHexStringLower(bytes.AsSpan(0, 8))}::/64");
    }

    private static RateLimitPartition<string> Partition(HttpContext context, RateLimitingOptions options)
    {
        PathString path = context.Request.Path;
        if (path.StartsWithSegments("/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            return Limited(POLICY_LOGIN, ClientKey(context), options.Login);
        }

        if (path.StartsWithSegments("/auth/refresh", StringComparison.OrdinalIgnoreCase))
        {
            return Limited(POLICY_REFRESH, ClientKey(context), options.Refresh);
        }

        if (path.StartsWithSegments("/auth/services", StringComparison.OrdinalIgnoreCase))
        {
            // Токен сервиса уже проверен (подпись, срок, отзыв): client_id в нём настоящий. Человек, чужой токен и запрос без токена
            // ключа сервиса не имеют — их считаем по IP.
            string? serviceClientId = context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirst(AuthClaims.CLIENT_ID)?.Value
                : null;
            return string.IsNullOrEmpty(serviceClientId)
                ? Limited(POLICY_SERVICES_ANONYMOUS, ClientKey(context), options.ServicesAnonymous)
                : Limited(POLICY_SERVICES, serviceClientId, options.Services);
        }

        return RateLimitPartition.GetNoLimiter("none");
    }

    private static RateLimitPartition<string> Limited(string policy, string key, RateLimitRule rule) =>
        rule.PermitLimit <= 0
            ? RateLimitPartition.GetNoLimiter($"{policy}:off")
            : RateLimitPartition.GetSlidingWindowLimiter(
                $"{policy}:{key}",
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = rule.PermitLimit,
                    Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                    SegmentsPerWindow = Math.Min(SEGMENTS_PER_WINDOW, rule.WindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        HttpContext http = context.HttpContext;
        RateLimitingOptions options = http.RequestServices.GetRequiredService<RateLimitingOptions>();
        string policy = PolicyOf(http.Request.Path, http.User);

        int retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan wait)
            ? (int)Math.Ceiling(wait.TotalSeconds)
            : options.WindowOf(policy);
        retryAfter = Math.Max(1, retryAfter);

        AuthMetrics.RateLimited(policy);
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers[HeaderNames.RetryAfter] = retryAfter.ToString(CultureInfo.InvariantCulture);
        await http.Response.WriteAsJsonAsync(
            new
            {
                result = (object?)null,
                error = new
                {
                    messages = new[]
                    {
                        new
                        {
                            code = "auth.rate_limited",
                            message = $"Слишком много запросов. Повторите через {retryAfter} с.",
                            invalidField = (string?)null,
                        },
                    },
                    type = "RATE_LIMIT",
                },
                isError = true,
                traceId = TraceContext.CurrentId(http),
            },
            cancellationToken);
    }

    private static string PolicyOf(PathString path, System.Security.Claims.ClaimsPrincipal user)
    {
        if (path.StartsWithSegments("/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            return POLICY_LOGIN;
        }

        if (path.StartsWithSegments("/auth/refresh", StringComparison.OrdinalIgnoreCase))
        {
            return POLICY_REFRESH;
        }

        return user.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(user.FindFirst(AuthClaims.CLIENT_ID)?.Value)
            ? POLICY_SERVICES
            : POLICY_SERVICES_ANONYMOUS;
    }
}

/// <summary>Лимит: не больше PermitLimit запросов за WindowSeconds секунд; PermitLimit 0 — без лимита.</summary>
public sealed class RateLimitRule
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; } = 60;
}

/// <summary>Секция RateLimiting. Значения по умолчанию — для проекта с сотнями людей; подберите под свою нагрузку.</summary>
public sealed class RateLimitingOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Начало входа (GET /auth/login) с одного IP: человек нажимает «Войти» несколько раз, а не сотни.</summary>
    public RateLimitRule Login { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    /// <summary>Обновление токенов (POST /auth/refresh) с одного IP: одна вкладка обновляет раз в минуты, но за NAT живут многие.</summary>
    public RateLimitRule Refresh { get; set; } = new() { PermitLimit = 120, WindowSeconds = 60 };

    /// <summary>API для сервисов (/auth/services/*) на один ключ сервиса.</summary>
    public RateLimitRule Services { get; set; } = new() { PermitLimit = 600, WindowSeconds = 60 };

    /// <summary>Запросы к /auth/services/* без действующего токена сервиса — по IP: подбор и мусор не должны занимать чужие лимиты.</summary>
    public RateLimitRule ServicesAnonymous { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    /// <summary>Ошибка в настройке останавливает старт: молча выключенный или нулевой лимит хуже, чем отказ запуститься.</summary>
    public void Validate()
    {
        foreach ((string name, RateLimitRule rule) in new[]
        {
            (nameof(Login), Login), (nameof(Refresh), Refresh), (nameof(Services), Services), (nameof(ServicesAnonymous), ServicesAnonymous),
        })
        {
            if (rule.PermitLimit < 0)
            {
                throw new InvalidOperationException($"{RateLimitingConfiguration.SECTION}:{name}:PermitLimit — не меньше 0 (0 — без лимита).");
            }

            if (rule.WindowSeconds is < 1 or > 86400)
            {
                throw new InvalidOperationException($"{RateLimitingConfiguration.SECTION}:{name}:WindowSeconds — от 1 до 86400 секунд.");
            }
        }
    }

    internal int WindowOf(string policy) =>
        policy switch
        {
            RateLimitingConfiguration.POLICY_LOGIN => Login.WindowSeconds,
            RateLimitingConfiguration.POLICY_REFRESH => Refresh.WindowSeconds,
            RateLimitingConfiguration.POLICY_SERVICES => Services.WindowSeconds,
            _ => ServicesAnonymous.WindowSeconds,
        };
}
