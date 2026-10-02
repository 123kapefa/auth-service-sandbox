import { toast } from "sonner";
import { getErrorCode, getErrorMessage } from "@/shared/api";
import { loginUrl } from "@/shared/config/routes";

/** Код сервиса: действие требует недавнего входа (выдать роль с управлением ролями, отключить 2FA). */
export const REAUTHENTICATION_REQUIRED = "auth.reauthentication_required";

/**
 * Ошибка действия — уведомлением «Ошибка {действия}: …». Нужен повторный вход — с кнопкой «Войти заново»: Keycloak
 * спросит пароль (и код 2FA), потом вернёт сюда же.
 */
export function notifyError(action: string, error: unknown): void {
  if (getErrorCode(error) === REAUTHENTICATION_REQUIRED) {
    toast.error(`Ошибка: ${action}`, {
      description: "Подтвердите, что это вы: войдите заново и повторите действие.",
      action: {
        label: "Войти заново",
        onClick: () => window.location.assign(loginUrl(window.location.pathname, true)),
      },
      duration: 15_000,
    });
    return;
  }

  toast.error(`Ошибка: ${action}`, { description: getErrorMessage(error, "Попробуйте ещё раз") });
}

/** Успех — коротко, без «успешно». */
export function notifyDone(message: string): void {
  toast.success(message);
}
