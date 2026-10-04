import { type AppBrandingOwn, type Branding, type BrandingTheme } from "./types";

/** Как на сервере (BrandingRules): название — до 60 символов, логотип — до 100 КБ. */
export const BRANDING_NAME_MAX_LENGTH = 60;
export const BRANDING_LOGO_MAX_BYTES = 100 * 1024;
export const BRANDING_LOGO_TYPES = ["image/png", "image/jpeg", "image/webp", "image/svg+xml"];

export const BRANDING_THEMES: { value: BrandingTheme; label: string }[] = [
  { value: "system", label: "Как в системе" },
  { value: "light", label: "Светлая" },
  { value: "dark", label: "Тёмная" },
];

/** Палитры серого стиля (те же токены, что в кабинете и на страницах входа). */
export const PALETTES = {
  light: {
    background: "#f7f7f7",
    card: "#eeeeee",
    foreground: "#2e2e2e",
    muted: "#6a6a6a",
    border: "#d0d0d0",
    primary: "#3c3c3c",
    shadow: "#c4c4c4",
  },
  dark: {
    background: "#1c1c1c",
    card: "#262626",
    foreground: "#dedede",
    muted: "#9a9a9a",
    border: "#3d3d3d",
    primary: "#cdcdcd",
    shadow: "#474747",
  },
} as const;

/**
 * Цвет текста на заливке цветом проекта: тёмный на светлом, светлый на тёмном (по яркости WCAG). Так же считает тема
 * входа (login-theme/src/login/branding.ts).
 */
export function textOn(color: string): string {
  const linear = (i: number) => {
    const channel = parseInt(color.slice(i, i + 2), 16) / 255;
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  };
  const luminance = 0.2126 * linear(1) + 0.7152 * linear(3) + 0.0722 * linear(5);
  // Контраст с #f7f7f7 и с #2e2e2e равен при яркости ≈ 0.22: выше — тёмный текст.
  return luminance > 0.22 ? "#2e2e2e" : "#f7f7f7";
}

/** Файл логотипа → data URL или ошибка по-русски (тип и размер — как проверяет сервер). */
export async function readLogo(file: File): Promise<{ logo: string } | { error: string }> {
  if (!BRANDING_LOGO_TYPES.includes(file.type)) {
    return { error: "Нужна картинка PNG, JPEG, WebP или SVG" };
  }

  if (file.size > BRANDING_LOGO_MAX_BYTES) {
    return { error: "Логотип — не больше 100 КБ" };
  }

  return { logo: await readAsDataUrl(file) };
}

/** Итоговое оформление приложения: проект, поверх — заданное у приложения (тема — проекта). */
export function effectiveBranding(project: Branding, own: AppBrandingOwn): Branding {
  return {
    name: own.name ?? project.name,
    logo: own.logo ?? project.logo,
    color: own.color ?? project.color,
    theme: project.theme,
  };
}

/**
 * Цвет проекта (приложения) — в токены primary и ring поверх темы, текст на цвете — по яркости; без цвета — серые
 * токены темы. Как на страницах входа (login-theme/src/login/branding.ts).
 */
export function applyBrandingColor(
  color: string | null,
  root: HTMLElement = document.documentElement,
): void {
  if (color) {
    root.style.setProperty("--color-primary", color);
    root.style.setProperty("--color-ring", color);
    root.style.setProperty("--color-primary-foreground", textOn(color));
  } else {
    root.style.removeProperty("--color-primary");
    root.style.removeProperty("--color-ring");
    root.style.removeProperty("--color-primary-foreground");
  }
}

/** Файл картинки → data URL для логотипа. */
export function readAsDataUrl(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error ?? new Error("Не удалось прочитать файл"));
    reader.readAsDataURL(file);
  });
}
