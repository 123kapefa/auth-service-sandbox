import { accountApp } from "@/shared/lib/account-app";

/** Админка живёт на /admin сервиса входа (base в vite.config, basename роутера). */
export const ADMIN_BASE = "/admin";

/** Личный кабинет — на /account сервиса входа (vite --mode account). */
export const ACCOUNT_BASE = "/account";

/**
 * Действие с учётной записью на странице сервера входа (тема проекта): пароль, почта, 2FA и ключи доступа вводятся и
 * подтверждаются там и не проходят через страницу кабинета.
 */
export type AccountAction =
  | "update-password"
  | "update-email"
  | "configure-totp"
  | "register-passkey"
  | "configure-recovery-codes"
  | "delete-credential";

/**
 * Вход с действием: Keycloak покажет страницу действия и вернёт сюда с action_status (success, cancelled, error).
 * credential — что удалить (для delete-credential).
 */
export function accountActionUrl(
  action: AccountAction,
  returnTo: string,
  credential?: string,
): string {
  const params = new URLSearchParams({ return_to: returnTo, action });
  if (credential) {
    params.set("credential", credential);
  }

  // Кабинет открыт из приложения — и страница действия в его оформлении.
  const app = accountApp();
  if (app) {
    params.set("app", app);
  }

  return `/auth/login?${params.toString()}`;
}

/** Вход через сервис: после входа Keycloak вернёт сюда (адрес проверяет сервис — ReturnUrls). */
export function loginUrl(returnTo: string, reauth = false): string {
  const params = new URLSearchParams({ return_to: returnTo });
  if (reauth) {
    params.set("reauth", "true");
  }

  return `/auth/login?${params.toString()}`;
}
