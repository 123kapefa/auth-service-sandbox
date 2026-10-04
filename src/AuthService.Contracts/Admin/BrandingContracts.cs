namespace AuthService.Contracts.Admin;

/// <summary>
/// Оформление проекта на страницах входа — GET /auth/admin/branding.
/// </summary>
/// <param name="Name">Название проекта (над формой входа, в заголовке вкладки).</param>
/// <param name="Logo">Логотип — data URL (PNG, JPEG, WebP, SVG); null — без логотипа.</param>
/// <param name="Color">Цвет — #rrggbb; null — серый стиль без цвета.</param>
/// <param name="Theme">Тема страниц: system (как в системе), light или dark.</param>
public sealed record BrandingResponse(string Name, string? Logo, string? Color, string Theme);

/// <summary>PUT /auth/admin/branding — сохранить оформление целиком (null у логотипа и цвета — убрать).</summary>
public sealed record SetBrandingRequest(string? Name, string? Logo, string? Color, string? Theme);

/// <summary>
/// Оформление приложения — GET /auth/admin/apps/{clientId}/branding: своё (null — как у проекта) и проекта, чтобы
/// показать итог.
/// </summary>
/// <param name="ClientId">Приложение.</param>
/// <param name="Name">Своё название на страницах входа и в письмах; null — как у проекта.</param>
/// <param name="Logo">Свой логотип — data URL; null — как у проекта.</param>
/// <param name="Color">Свой цвет — #rrggbb; null — как у проекта.</param>
/// <param name="Project">Оформление проекта (тема — всегда его).</param>
public sealed record AppBrandingResponse(string ClientId, string? Name, string? Logo, string? Color, BrandingResponse Project);

/// <summary>PUT /auth/admin/apps/{clientId}/branding — своё у приложения целиком (null — как у проекта).</summary>
public sealed record SetAppBrandingRequest(string? Name, string? Logo, string? Color);
