using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Abstractions;

/// <summary>
/// Оформление проекта на сервере входа: название realm и логотип, цвет, тема страниц входа (плагин Keycloak
/// platform-branding, атрибуты realm). Страницы входа читают его при открытии — пересборка темы не нужна.
/// </summary>
public interface IProjectBranding
{
    Task<Result<ProjectBranding, Error>> GetAsync(CancellationToken cancellationToken);

    Task<UnitResult<Error>> SetAsync(ProjectBranding branding, CancellationToken cancellationToken);
}

/// <summary>Оформление проекта.</summary>
/// <param name="Name">Название проекта.</param>
/// <param name="Logo">Логотип — data URL; null — нет.</param>
/// <param name="Color">Цвет #rrggbb (строчные); null — серый стиль.</param>
/// <param name="Theme">BrandingThemes: system, light, dark.</param>
public sealed record ProjectBranding(string Name, string? Logo, string? Color, string Theme);

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
