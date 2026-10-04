namespace AuthService.Core.Features.Security;

/// <summary>
/// Подпись устройства по User-Agent: «Chrome · macOS», «Safari · iPhone». Без библиотеки разбора: нужны только
/// браузер и система, а порядок проверок учитывает, что Edge, Opera и Яндекс Браузер представляются Chrome, а
/// Chrome — Safari. Не узнали ни браузер, ни систему — null (кабинет покажет «Неизвестное устройство»).
/// </summary>
public static class DeviceLabels
{
    private static readonly (string Token, string Name)[] _browsers =
    [
        ("YaBrowser/", "Яндекс Браузер"),
        ("Edg/", "Edge"),
        ("EdgA/", "Edge"),
        ("EdgiOS/", "Edge"),
        ("OPR/", "Opera"),
        ("SamsungBrowser/", "Samsung Internet"),
        ("Firefox/", "Firefox"),
        ("FxiOS/", "Firefox"),
        ("CriOS/", "Chrome"),
        ("Chrome/", "Chrome"),
        ("Safari/", "Safari"),
    ];

    // Мобильные — раньше настольных: в User-Agent Android есть «Linux», в iPhone — «like Mac OS X».
    private static readonly (string Token, string Name)[] _systems =
    [
        ("iPhone", "iPhone"),
        ("iPad", "iPad"),
        ("Android", "Android"),
        ("Windows", "Windows"),
        ("CrOS", "ChromeOS"),
        ("Macintosh", "macOS"),
        ("Mac OS X", "macOS"),
        ("Linux", "Linux"),
    ];

    public static string? FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        string? browser = Find(_browsers, userAgent);
        string? system = Find(_systems, userAgent);
        return (browser, system) switch
        {
            (null, null) => null,
            (null, _) => system,
            (_, null) => browser,
            _ => $"{browser} · {system}",
        };
    }

    private static string? Find((string Token, string Name)[] known, string userAgent) =>
        known.FirstOrDefault(item => userAgent.Contains(item.Token, StringComparison.Ordinal)).Name;
}
