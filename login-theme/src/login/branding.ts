/**
 * Оформление на страницах входа: название, логотип, акцент (для светлой и тёмной темы) и тема — из плагина Keycloak platform-branding
 * (GET /realms/{realm}/platform-branding): проекта (атрибуты realm) и поверх него — приложения, в которое входят
 * (название, логотип, акцент). Приложение плагин узнаёт по клиенту входа и сессии входа этой вкладки (client_id, tab_id
 * и cookie сессии входа). Меняется в админке без пересборки темы: страница читает его при открытии.
 */
export interface Branding {
  name: string;
  logo: string | null;
  /** Акцент — кнопки, отметки и фокус; null — серый стиль. */
  color: string | null;
  /** Акцент в тёмной теме; null — тот же, что color. */
  colorDark: string | null;
  theme: "system" | "light" | "dark";
}

const isColor = (value: unknown): value is string =>
  typeof value === "string" && /^#[0-9a-f]{6}$/.test(value);

/** Не дождались за это время — страница показывается в сером стиле: вход важнее оформления. */
const TIMEOUT_MS = 1500;

let current: Branding | null = null;

/** Оформление, загруженное перед первым показом страницы (null — не загрузилось или макет без Keycloak). */
export const getBranding = () => current;

/** Чья страница входа: клиент входа и вкладка (tab_id — из адреса формы, kcContext.url.loginAction). */
export interface BrandingTarget {
  clientId?: string;
  loginAction?: string;
}

/**
 * Адрес плагина — рядом со страницей входа: Keycloak показывает её по адресу …/realms/{realm}/…; client_id и tab_id —
 * чтобы плагин нашёл приложение, в которое входят.
 */
export function brandingUrl(pathname: string, realm: string, target: BrandingTarget = {}): string {
  const base =
    /^(.*?\/realms\/[^/]+\/)/.exec(pathname)?.[1] ?? `/realms/${encodeURIComponent(realm)}/`;
  const query = new URLSearchParams();
  if (target.clientId) {
    query.set("client_id", target.clientId);
  }

  const tabId = target.loginAction && /[?&]tab_id=([^&#]+)/.exec(target.loginAction)?.[1];
  if (tabId) {
    query.set("tab_id", decodeURIComponent(tabId));
  }

  const search = query.toString();
  return `${base}platform-branding${search ? `?${search}` : ""}`;
}

export async function loadBranding(
  realm: string,
  target: BrandingTarget = {},
): Promise<Branding | null> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
  try {
    // same-origin: cookie сессии входа — по ней плагин узнаёт приложение, если вход через сервис входа (?app=…).
    const response = await fetch(brandingUrl(window.location.pathname, realm, target), {
      signal: controller.signal,
      credentials: "same-origin",
    });
    if (!response.ok) {
      return null;
    }

    const body = (await response.json()) as Partial<Branding>;
    current = {
      name: body.name ?? "",
      logo: typeof body.logo === "string" && body.logo.startsWith("data:image/") ? body.logo : null,
      color: isColor(body.color) ? body.color : null,
      colorDark: isColor(body.color) && isColor(body.colorDark) ? body.colorDark : null,
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
 * Тема — data-theme на <html> (как в кабинете: светлые и тёмные токены в styles.css), акцент — токены accent и ring
 * поверх темы: светлый — в светлой, тёмный (или тот же) — в тёмной; линии, рамки и ошибки остаются своими. Текст на
 * акценте — тёмный или светлый по яркости (как в кабинете, frontend/src/entities/branding).
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
    // CSSOM, не <style>: не зависит от CSP страницы (как в кабинете).
    const sheet = new CSSStyleSheet();
    sheet.replaceSync(accentCss(branding.color, branding.colorDark ?? branding.color));
    const doc = root.ownerDocument;
    // До первой записи в jsdom (тесты) списка ещё нет.
    const adopted = doc.adoptedStyleSheets as CSSStyleSheet[] | undefined;
    doc.adoptedStyleSheets = [...(adopted ?? []), sheet];
  }
}

/** Правила акцента: светлый — для светлой темы, тёмный — для тёмной (закреплённой или системной). */
export function accentCss(light: string, dark: string): string {
  const tokens = (color: string) =>
    `--color-accent:${color};--color-accent-foreground:${textOn(color)};--color-ring:${color};`;
  return [
    `:root{${tokens(light)}}`,
    `:root[data-theme="dark"]{${tokens(dark)}}`,
    `@media (prefers-color-scheme: dark){:root:not([data-theme="light"]){${tokens(dark)}}}`,
  ].join("\n");
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
