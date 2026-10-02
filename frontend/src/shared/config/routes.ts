/** Админка живёт на /admin сервиса входа (base в vite.config, basename роутера). */
export const ADMIN_BASE = "/admin";

/** Вход через сервис: после входа Keycloak вернёт сюда (адрес проверяет сервис — ReturnUrls). */
export function loginUrl(returnTo: string, reauth = false): string {
  const params = new URLSearchParams({ return_to: returnTo });
  if (reauth) {
    params.set("reauth", "true");
  }

  return `/auth/login?${params.toString()}`;
}
