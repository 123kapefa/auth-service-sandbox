using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace AuthService.Core.Features.Auth.ReturnUrls;

/// <summary>
/// Разрешает относительный путь этого сервиса или абсолютный адрес с origin из белого списка.
/// Всё остальное (чужой домен, //evil.com, javascript:, user@host, управляющие и невидимые символы) заменяется адресом по умолчанию.
/// </summary>
public sealed class ReturnUrlValidator(IOptions<ReturnUrlOptions> options)
{
    private readonly ReturnUrlOptions _options = options.Value;

    public string Resolve(string? returnTo) => IsAllowed(returnTo) ? returnTo! : _options.Default;

    public bool IsAllowed(string? returnTo)
    {
        if (string.IsNullOrWhiteSpace(returnTo) || ContainsHiddenCharacters(returnTo))
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

    /// <summary>
    /// Адрес для заголовка Location. В заголовке допустим только ASCII (Kestrel иначе отвечает 500 уже после успешного входа),
    /// поэтому буквы не из ASCII ("/заказы/1") записываются процентами в UTF-8; остальное остаётся как есть. Хост из белого
    /// списка с не-ASCII (IDN) тоже уйдёт процентами — браузер понимает такую запись. Принимает только проверенный адрес
    /// (после Resolve): сам CR/LF и прочее не отсекает.
    /// </summary>
    public static string ToLocation(string returnTo)
    {
        ArgumentNullException.ThrowIfNull(returnTo);
        return string.Concat(returnTo.EnumerateRunes().Select(static rune =>
            rune.IsAscii ? rune.ToString() : Uri.EscapeDataString(rune.ToString())));
    }

    // Браузер выбрасывает TAB, LF и CR из адреса, поэтому "/<TAB>/evil.example" он читает как "//evil.example" — чужой хост.
    // Значение приходит уже декодированным (%09 → TAB) — из query и из cookie состояния входа. Управляющие символы (в том числе CR/LF, из-за которых Location
    // не отправляется), любые пробелы и невидимые символы (нулевой ширины, направления письма) в адресе возврата не нужны.
    private static bool ContainsHiddenCharacters(string value) =>
        value.EnumerateRunes().Any(static rune =>
            Rune.IsControl(rune)
            || Rune.IsWhiteSpace(rune)
            || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    private static bool IsSameOrigin(Uri uri, string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out Uri? allowed)
        && string.Equals(uri.Scheme, allowed.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, allowed.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == allowed.Port;
}
