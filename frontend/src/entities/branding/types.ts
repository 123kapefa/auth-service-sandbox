/** Тема страниц входа: как в системе посетителя, всегда светлая или всегда тёмная. */
export type BrandingTheme = "system" | "light" | "dark";

/** Оформление проекта на страницах входа (GET/PUT /auth/admin/branding). */
export interface Branding {
  /** Название проекта — над формой входа и в заголовке вкладки. */
  name: string;
  /** Логотип — data URL (PNG, JPEG, WebP, SVG); null — без логотипа. */
  logo: string | null;
  /** Цвет — #rrggbb; null — серый стиль без цвета. */
  color: string | null;
  theme: BrandingTheme;
}

/** Своё у приложения поверх проекта: null — как у проекта. */
export interface AppBrandingOwn {
  name: string | null;
  logo: string | null;
  color: string | null;
}

/** GET /auth/admin/apps/{clientId}/branding: своё у приложения и оформление проекта. */
export interface AppBranding extends AppBrandingOwn {
  clientId: string;
  project: Branding;
}
