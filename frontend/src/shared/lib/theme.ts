export type Theme = "light" | "dark";

const STORAGE_KEY = "theme";

function stored(): Theme | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === "light" || value === "dark" ? value : null;
  } catch {
    return null;
  }
}

/** Тема по умолчанию от проекта (оформление: «всегда светлая/тёмная»); null — как в системе. */
let fallback: Theme | null = null;

/** Тема сейчас: выбор пользователя, иначе тема проекта, иначе настройка системы. */
export function currentTheme(): Theme {
  return (
    stored() ??
    fallback ??
    (window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light")
  );
}

/** Тема проекта — пока пользователь не выбрал свою переключателем. */
export function setDefaultTheme(theme: Theme | null): void {
  fallback = theme;
  if (stored()) {
    return;
  }

  if (theme) {
    document.documentElement.dataset.theme = theme;
  } else {
    delete document.documentElement.dataset.theme;
  }
}

/** Выбор пользователя — на <html data-theme>; без выбора тему берёт CSS из настройки системы. */
export function applyStoredTheme(): void {
  const theme = stored();
  if (theme) {
    document.documentElement.dataset.theme = theme;
  }
}

export function setTheme(theme: Theme): void {
  document.documentElement.dataset.theme = theme;
  try {
    localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    // Хранилище недоступно (приватный режим) — тема продержится до перезагрузки.
  }
}
