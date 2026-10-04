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

/** Тема сейчас: выбор пользователя, иначе настройка системы. */
export function currentTheme(): Theme {
  return (
    stored() ?? (window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light")
  );
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
