namespace PlatformAuth;

/// <summary>Имена cookie с токенами — контракт между сервисом входа и остальными сервисами.</summary>
public static class AuthCookies
{
    /// <summary>Access-токен (JWT). Любой сервис читает его вместо заголовка Authorization.</summary>
    public const string ACCESS_TOKEN = "accessToken";

    /// <summary>Refresh-токен. Нужен только сервису входа.</summary>
    public const string REFRESH_TOKEN = "refreshToken";

    /// <summary>
    /// Приставка имён по https (Cookies:Secure=true): браузер принимает такую cookie только с Secure, Path=/ и без Domain, то
    /// есть её нельзя подменить с соседнего поддомена (cookie tossing: страница на взломанном поддомене ставит cookie с
    /// Domain=родитель и тем же именем — без приставки сервис не отличил бы её от своей).
    /// </summary>
    public const string HOST_PREFIX = "__Host-";

    /// <summary>Имя cookie с access-токеном: по https — с приставкой, локально по http — без (Secure там невозможна).</summary>
    public static string AccessToken(bool hostPrefix) => hostPrefix ? HOST_PREFIX + ACCESS_TOKEN : ACCESS_TOKEN;

    /// <summary>Имя cookie с refresh-токеном: по https — с приставкой, локально по http — без.</summary>
    public static string RefreshToken(bool hostPrefix) => hostPrefix ? HOST_PREFIX + REFRESH_TOKEN : REFRESH_TOKEN;
}
