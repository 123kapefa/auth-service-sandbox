/** Админка живёт на /admin сервиса входа (base в vite.config, basename роутера). */
export const ADMIN_BASE = "/admin";

/** Личный кабинет — на /account сервиса входа (vite --mode account). */
export const ACCOUNT_BASE = "/account";

/** 2FA и ключи доступа: постоянная ссылка сервиса (пока — раздел «Вход» кабинета Keycloak, свой — этап 6б.2). */
export const ACCOUNT_SECURITY_URL = "/auth/account/security";

/** Действие с учётной записью на странице сервера входа (тема проекта): пароль и почта не проходят через страницу. */
export type AccountAction = "update-password" | "update-email";

/** Вход с действием: Keycloak покажет страницу действия и вернёт сюда с action_status (success, cancelled, error). */
export function accountActionUrl(action: AccountAction, returnTo: string): string {
  return `/auth/login?${new URLSearchParams({ return_to: returnTo, action }).toString()}`;
}

/** Вход через сервис: после входа Keycloak вернёт сюда (адрес проверяет сервис — ReturnUrls). */
export function loginUrl(returnTo: string, reauth = false): string {
  const params = new URLSearchParams({ return_to: returnTo });
  if (reauth) {
    params.set("reauth", "true");
  }

  return `/auth/login?${params.toString()}`;
}
