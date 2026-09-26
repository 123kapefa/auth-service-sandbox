using Microsoft.Extensions.Options;

namespace AuthService.Core.Features.Auth.ReturnUrls;

/// <summary>
/// Разрешает относительный путь этого сервиса или абсолютный адрес с origin из белого списка.
/// Всё остальное (чужой домен, //evil.com, javascript:, user@host) заменяется адресом по умолчанию.
/// </summary>
public sealed class ReturnUrlValidator(IOptions<ReturnUrlOptions> options)
{
    private readonly ReturnUrlOptions _options = options.Value;

    public string Resolve(string? returnTo) => IsAllowed(returnTo) ? returnTo! : _options.Default;

    public bool IsAllowed(string? returnTo)
    {
        if (string.IsNullOrWhiteSpace(returnTo))
        {
            return false;
        }

        // "/path" — да; "//evil.com" и "/\evil.com" браузер понимает как другой хост — нет.
        if (returnTo.StartsWith('/'))
        {
            return returnTo.Length == 1 || (returnTo[1] != '/' && returnTo[1] != '\\');
        }

        return Uri.TryCreate(returnTo, UriKind.Absolute, out Uri? uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
            && string.IsNullOrEmpty(uri.UserInfo)
            && _options.AllowedOrigins.Any(origin => IsSameOrigin(uri, origin));
    }

    private static bool IsSameOrigin(Uri uri, string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out Uri? allowed)
        && string.Equals(uri.Scheme, allowed.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, allowed.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == allowed.Port;
}
