import {
  type ProjectMethod,
  type ProjectMethodState,
  type ProjectSignIn,
  REGISTRATION_METHODS,
} from "@/entities/sign-in-settings";

/** Те же способы и 2FA, один способ — в новом состоянии. */
export const withMethod = (
  settings: ProjectSignIn,
  method: ProjectMethod,
  state: ProjectMethodState,
): ProjectSignIn => ({ ...settings, methods: { ...settings.methods, [method]: state } });

/**
 * Останется ли способ регистрации, если выключить method (в переходный период новых тоже не регистрируем). Без него
 * сервис не сохранит (`sign_in.no_registration`).
 */
export const keepsRegistration = (settings: ProjectSignIn, method: ProjectMethod): boolean =>
  REGISTRATION_METHODS.some((other) => other !== method && settings.methods[other].status === "on");

/** Каким станет поле логина на странице входа, если выключить телефон или почту; null — не меняется. */
export function loginFieldAfter(settings: ProjectSignIn, method: ProjectMethod): string | null {
  if (method === "phone" && settings.methods.email.status !== "off") {
    return "Почта";
  }

  if (method === "email" && settings.methods.phone.status !== "off") {
    return "Номер телефона";
  }

  return null;
}

const iso = (date: Date) =>
  `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;

/** Дата через days дней, yyyy-MM-dd (по часам браузера; сервис проверит по Москве). */
export function daysAhead(days: number, now = new Date()): string {
  const date = new Date(now);
  date.setDate(date.getDate() + days);
  return iso(date);
}

/** yyyy-MM-dd → «05.11.2026». */
export function formatDay(value: string): string {
  const [year, month, day] = value.split("-");
  return `${day}.${month}.${year}`;
}
