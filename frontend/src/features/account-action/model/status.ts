import { type AccountAction } from "@/shared/config/routes";

export type ActionStatus = "success" | "cancelled" | "error";

/** Параметр адреса, в котором сервис возвращает итог действия. */
export const ACTION_STATUS_PARAM = "action_status";

/** Параметр, по которому страница помнит, какое действие запускала. */
export const ACTION_PARAM = "action";

const done: Record<AccountAction, string> = {
  "update-password": "Пароль изменён",
  // Keycloak с подтверждением почты сюда не возвращает (страница «письмо отправлено»), но если вернёт — почта ещё
  // прежняя: сменится после перехода по ссылке.
  "update-email": "Письмо отправлено — почта сменится после перехода по ссылке из него",
  "configure-totp": "Приложение-аутентификатор подключено",
  "register-passkey": "Ключ доступа добавлен",
  "configure-recovery-codes": "Коды восстановления созданы — храните их отдельно от телефона",
  "delete-credential": "Удалено",
};

/** Что сказать пользователю после возврата со страницы действия; null — нечего (отменил или нет итога). */
export function actionNotice(
  action: string | null,
  status: string | null,
): { kind: "success" | "error"; text: string } | null {
  if (status === "success") {
    return { kind: "success", text: done[action as AccountAction] ?? "Готово" };
  }

  if (status === "error") {
    return { kind: "error", text: "Не получилось — попробуйте ещё раз" };
  }

  return null;
}
