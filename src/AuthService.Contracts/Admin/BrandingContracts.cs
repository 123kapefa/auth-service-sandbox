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
