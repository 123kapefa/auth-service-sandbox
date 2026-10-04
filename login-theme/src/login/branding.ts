/**
 * Оформление проекта на страницах входа: логотип, цвет и тема — из плагина Keycloak platform-branding
 * (GET /realms/{realm}/platform-branding, атрибуты realm), название — displayName realm (уже в kcContext). Меняется в
 * админке проекта без пересборки темы: страница читает его при открытии.
 */
export interface Branding {
  name: string;
  logo: string | null;
  color: string | null;
  theme: "system" | "light" | "dark";
}

/** Не дождались за это время — страница показывается в сером стиле: вход важнее оформления. */
const TIMEOUT_MS = 1500;

let current: Branding | null = null;

/** Оформление, загруженное перед первым показом страницы (null — не загрузилось или макет без Keycloak). */
export const getBranding = () => current;

/** Адрес плагина — рядом со страницей входа: Keycloak показывает её по адресу …/realms/{realm}/…. */
export function brandingUrl(pathname: string, realm: string): string {
  const base =
    /^(.*?\/realms\/[^/]+\/)/.exec(pathname)?.[1] ?? `/realms/${encodeURIComponent(realm)}/`;
  return `${base}platform-branding`;
}

export async function loadBranding(realm: string): Promise<Branding | null> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
  try {
    const response = await fetch(brandingUrl(window.location.pathname, realm), {
      signal: controller.signal,
      credentials: "omit",
    });
    if (!response.ok) {
      return null;
    }

    const body = (await response.json()) as Partial<Branding>;
    current = {
      name: body.name ?? "",
      logo: typeof body.logo === "string" && body.logo.startsWith("data:image/") ? body.logo : null,
      color:
        typeof body.color === "string" && /^#[0-9a-f]{6}$/.test(body.color) ? body.color : null,
      theme: body.theme === "light" || body.theme === "dark" ? body.theme : "system",
    };
    return current;
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Тема — data-theme на <html> (как в кабинете: светлые и тёмные токены в styles.css), цвет — токены primary и ring
 * поверх темы; текст на цвете — тёмный или светлый по яркости (как в админке, frontend/src/entities/branding).
 */
export function applyBranding(
  branding: Branding,
  root: HTMLElement = document.documentElement,
): void {
  if (branding.theme === "system") {
    delete root.dataset.theme;
  } else {
    root.dataset.theme = branding.theme;
  }

  if (branding.color) {
    root.style.setProperty("--color-primary", branding.color);
    root.style.setProperty("--color-ring", branding.color);
    root.style.setProperty("--color-primary-foreground", textOn(branding.color));
  }
}

/** Цвет текста на заливке цветом проекта: тёмный на светлом, светлый на тёмном (яркость WCAG). */
export function textOn(color: string): string {
  const linear = (i: number) => {
    const channel = parseInt(color.slice(i, i + 2), 16) / 255;
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  };
  const luminance = 0.2126 * linear(1) + 0.7152 * linear(3) + 0.0722 * linear(5);
  // Контраст с #f7f7f7 и с #2e2e2e равен при яркости ≈ 0.22: выше — тёмный текст.
  return luminance > 0.22 ? "#2e2e2e" : "#f7f7f7";
}
