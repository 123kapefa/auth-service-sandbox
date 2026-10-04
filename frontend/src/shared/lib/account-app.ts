/**
 * Приложение, из которого открыли кабинет (/auth/account?app=clientId → /account/?app=…): кабинет в его оформлении,
 * и страницы действий (пароль, 2FA) — тоже. Запоминается на вкладку: дальше по разделам кабинета адрес без app.
 */
const STORAGE_KEY = "account-app";

/** Похоже на clientId — как проверяет сервер (BrandingRules.APP_PATTERN). */
const APP_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;

/** Если хранилище недоступно (приватный режим) — хотя бы до перезагрузки. */
let current: string | null = null;

/** Взять app из адреса (и убрать его оттуда), иначе — запомненное на этой вкладке. */
export function rememberAccountApp(location: Location = window.location): string | null {
  const url = new URL(location.href);
  const app = url.searchParams.get("app");
  if (app === null) {
    return accountApp();
  }

  url.searchParams.delete("app");
  window.history.replaceState(window.history.state, "", url.pathname + url.search + url.hash);
  if (!APP_PATTERN.test(app)) {
    return accountApp();
  }

  try {
    sessionStorage.setItem(STORAGE_KEY, app);
  } catch {
    current = app;
  }

  return app;
}

export function accountApp(): string | null {
  try {
    return sessionStorage.getItem(STORAGE_KEY);
  } catch {
    return current;
  }
}
