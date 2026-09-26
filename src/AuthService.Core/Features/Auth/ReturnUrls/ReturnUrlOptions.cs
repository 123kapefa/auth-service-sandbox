namespace AuthService.Core.Features.Auth.ReturnUrls;

/// <summary>Секция ReturnUrls: куда можно вернуть пользователя после входа и выхода.</summary>
public sealed class ReturnUrlOptions
{
    public const string SECTION_NAME = "ReturnUrls";

    /// <summary>Белый список origin'ов приложений (scheme://host[:port]). Защита от open redirect.</summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>Куда вернуть, если return_to нет или он не разрешён.</summary>
    public string Default { get; init; } = "/";
}
