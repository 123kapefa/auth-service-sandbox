using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Abstractions;

/// <summary>
/// Оформление проекта на сервере входа: название realm и логотип, цвет, тема страниц входа (плагин Keycloak
/// platform-branding, атрибуты realm). Приложение может переопределить название, логотип и цвет (атрибуты его
/// клиента). Страницы входа и письма читают его при открытии — пересборка темы не нужна.
/// </summary>
public interface IProjectBranding
{
    Task<Result<ProjectBranding, Error>> GetAsync(CancellationToken cancellationToken);

    Task<UnitResult<Error>> SetAsync(ProjectBranding branding, CancellationToken cancellationToken);

    /// <summary>Своё у приложения (null — как у проекта). Нет клиента — IdentityProviderErrors.AppNotFound.</summary>
    Task<Result<AppBranding, Error>> GetAppAsync(string clientId, CancellationToken cancellationToken);

    Task<UnitResult<Error>> SetAppAsync(string clientId, AppBranding branding, CancellationToken cancellationToken);

    /// <summary>Итоговое для показа: проект, поверх — приложение app (null или неизвестное — только проект).</summary>
    Task<Result<ProjectBranding, Error>> GetEffectiveAsync(string? app, CancellationToken cancellationToken);
}

/// <summary>Оформление проекта.</summary>
/// <param name="Name">Название проекта.</param>
/// <param name="Logo">Логотип — data URL; null — нет.</param>
/// <param name="Color">Цвет #rrggbb (строчные); null — серый стиль.</param>
/// <param name="Theme">BrandingThemes: system, light, dark.</param>
public sealed record ProjectBranding(string Name, string? Logo, string? Color, string Theme);

/// <summary>Оформление приложения поверх проекта: null — как у проекта.</summary>
/// <param name="Name">Название приложения на его страницах входа и в письмах.</param>
/// <param name="Logo">Логотип — data URL.</param>
/// <param name="Color">Цвет #rrggbb (строчные).</param>
public sealed record AppBranding(string? Name, string? Logo, string? Color)
{
    public static AppBranding AsProject { get; } = new(null, null, null);
}

/// <summary>Тема страниц входа и ограничения оформления — те же, что проверяет плагин Keycloak.</summary>
public static class BrandingRules
{
    public const string THEME_SYSTEM = "system";
    public const string THEME_LIGHT = "light";
    public const string THEME_DARK = "dark";

    public static IReadOnlyList<string> Themes { get; } = [THEME_SYSTEM, THEME_LIGHT, THEME_DARK];

    public const int NAME_MAX_LENGTH = 60;

    /// <summary>Логотип — до 100 КБ: он приходит на каждую страницу входа и хранится в realm.</summary>
    public const int LOGO_MAX_BYTES = 100 * 1024;

    public const string COLOR_PATTERN = "^#[0-9a-f]{6}$";

    public const string LOGO_PATTERN = "^data:image/(png|jpeg|webp|svg\\+xml);base64,(?<data>[A-Za-z0-9+/]+={0,2})$";

    /// <summary>clientId приложения в адресе (/auth/login?app=…, /me/branding?app=…): как у клиентов Keycloak проекта.</summary>
    public const string APP_PATTERN = "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$";
}

public static class BrandingErrors
{
    /// <summary>Плагин ответил 401/403: у служебной учётки нет роли manage-branding.</summary>
    public static Error NotPermitted() =>
        Error.Failure("branding.not_permitted", "Сервису входа не выдано право менять оформление (роль manage-branding в Keycloak).");

    /// <summary>Плагин ответил 404: в Keycloak нет плагина platform-branding (старый образ).</summary>
    public static Error NotInstalled() =>
        Error.Failure("branding.not_installed", "Оформление недоступно: в Keycloak нет плагина platform-branding.");

    /// <summary>Плагин не принял значения (обычно их уже отсёк сервис).</summary>
    public static Error Rejected(string message) =>
        Error.Validation("branding.invalid", message);
}

/// <summary>Приложение из адреса (?app=…) для оформления: только похожее на clientId, иначе — как без него.</summary>
public static partial class BrandingApps
{
    public static string? Normalize(string? app) => app is not null && AppPattern().IsMatch(app) ? app : null;

    [GeneratedRegex(BrandingRules.APP_PATTERN, RegexOptions.CultureInvariant)]
    private static partial Regex AppPattern();
}
