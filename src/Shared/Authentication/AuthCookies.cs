namespace PlatformAuth;

/// <summary>Имена cookie с токенами — контракт между сервисом входа и остальными сервисами.</summary>
public static class AuthCookies
{
    /// <summary>Access-токен (JWT). Любой сервис читает его вместо заголовка Authorization.</summary>
    public const string ACCESS_TOKEN = "accessToken";

    /// <summary>Refresh-токен. Нужен только сервису входа.</summary>
    public const string REFRESH_TOKEN = "refreshToken";
}
