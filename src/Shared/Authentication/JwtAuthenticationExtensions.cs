using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using PlatformAuth.Revocation;

namespace PlatformAuth;

public static class JwtAuthenticationExtensions
{
    /// <summary>Код ошибки 401, когда доступ изменился: клиенту — обновить токен (POST /auth/refresh) и повторить запрос.</summary>
    public const string ACCESS_CHANGED = "access_changed";

    private const string ACCESS_CHANGED_ITEM = "PlatformAuth.AccessChanged";

    /// <summary>Код ответа 503: хранилище отзывов недоступно, а режим при сбое — «закрыто».</summary>
    public const string REVOCATION_UNAVAILABLE = "revocation_unavailable";

    private const string REVOCATION_UNAVAILABLE_ITEM = "PlatformAuth.RevocationUnavailable";

    /// <summary>
    /// Проверка access-токенов сервера входа (Keycloak): подпись по ключам из discovery (RS256),
    /// iss, aud, срок действия. Настройки — секция Authentication, проверяются при старте.
    /// </summary>
    public static IServiceCollection AddPlatformJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtAuthenticationOptions>()
            .Bind(configuration.GetSection(JwtAuthenticationOptions.SECTION_NAME))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtAuthenticationOptions>, JwtAuthenticationOptionsValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtAuthenticationOptions>>((bearer, settings) => Configure(bearer, settings.Value));

        return services;
    }

    private static void Configure(JwtBearerOptions bearer, JwtAuthenticationOptions settings)
    {
        bearer.Authority = settings.Authority;
        if (settings.ResolveMetadataAddress() is { } metadata)
        {
            bearer.MetadataAddress = metadata;
        }

        bearer.RequireHttpsMetadata = settings.RequireHttpsMetadata;

        // Claims остаются как в токене (role, sub), без переименования в длинные URI .NET.
        bearer.MapInboundClaims = false;

        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.ResolveIssuer(),
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.Zero,
            NameClaimType = AuthClaims.SUBJECT,
            RoleClaimType = AuthClaims.ROLE,
        };

        bearer.Events = new JwtBearerEvents
        {
            OnMessageReceived = context => ReadTokenFromCookie(context, settings),
            OnTokenValidated = RejectRevokedAsync,
            OnChallenge = ExplainAccessChangedAsync,
        };
    }

    // Мгновенный отзыв: подпись и срок токена в порядке, но его отозвали (блокировка, завершённая сессия, закрытый
    // доступ к приложению) или доступ изменился (роли, состав роли). Проверка есть, только если сервис подключил
    // хранилище отзывов (AddPlatformTokenRevocation).
    private static async Task RejectRevokedAsync(TokenValidatedContext context)
    {
        ITokenRevocationChecker? checker = context.HttpContext.RequestServices.GetService<ITokenRevocationChecker>();
        if (checker is null || context.Principal is null)
        {
            return;
        }

        switch (await checker.CheckAsync(context.Principal, context.HttpContext.RequestAborted))
        {
            case TokenStatus.Revoked:
                context.Fail("The token has been revoked.");
                break;
            case TokenStatus.AccessChanged:
                context.HttpContext.Items[ACCESS_CHANGED_ITEM] = true;
                context.Fail("Access has changed: refresh the token.");
                break;
            case TokenStatus.Unavailable:
                context.HttpContext.Items[REVOCATION_UNAVAILABLE_ITEM] = true;
                context.Fail("The token revocation store is unavailable.");
                break;
            case TokenStatus.Valid:
            default:
                break;
        }
    }

    // Доступ изменился — 401 с объяснением: error_description=access_changed в WWW-Authenticate и код
    // auth.access_changed в теле. Клиент обновляет токен без перелогина и повторяет запрос.
    private static async Task ExplainAccessChangedAsync(JwtBearerChallengeContext context)
    {
        if (context.HttpContext.Items.ContainsKey(REVOCATION_UNAVAILABLE_ITEM))
        {
            await WriteRevocationUnavailableAsync(context);
            return;
        }

        if (!context.HttpContext.Items.ContainsKey(ACCESS_CHANGED_ITEM))
        {
            return;
        }

        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers[HeaderNames.WWWAuthenticate] = $"Bearer error=\"invalid_token\", error_description=\"{ACCESS_CHANGED}\"";
        await context.Response.WriteAsJsonAsync(
            new
            {
                result = (object?)null,
                error = new
                {
                    messages = new[]
                    {
                        new
                        {
                            code = "auth." + ACCESS_CHANGED,
                            message = "Доступ изменился: обновите токен (POST /auth/refresh) и повторите запрос.",
                            invalidField = (string?)null,
                        },
                    },
                    type = "AUTHENTICATION",
                },
                isError = true,
            },
            context.HttpContext.RequestAborted);
    }

    // Режим «закрыто» и хранилище отзывов недоступно: 503, а не 401 — человек не разлогинен, сервис временно не может проверить
    // токен; клиент повторяет запрос позже (Retry-After), а не гонит человека на вход.
    private static async Task WriteRevocationUnavailableAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers[HeaderNames.RetryAfter] = "5";
        await context.Response.WriteAsJsonAsync(
            new
            {
                result = (object?)null,
                error = new
                {
                    messages = new[]
                    {
                        new
                        {
                            code = "auth." + REVOCATION_UNAVAILABLE,
                            message = "Не удаётся проверить токен: хранилище отзывов недоступно. Повторите запрос позже.",
                            invalidField = (string?)null,
                        },
                    },
                    type = "FAILURE",
                },
                isError = true,
            },
            context.HttpContext.RequestAborted);
    }

    // Заголовок Authorization главнее: явно переданный токен (например, от другого сервиса)
    // не подменяется cookie, оставшейся в браузере.
    private static Task ReadTokenFromCookie(MessageReceivedContext context, JwtAuthenticationOptions settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.AccessTokenCookie)
            && !context.Request.Headers.ContainsKey(HeaderNames.Authorization)
            && context.Request.Cookies.TryGetValue(settings.AccessTokenCookie, out string? token)
            && !string.IsNullOrWhiteSpace(token))
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    }
}
