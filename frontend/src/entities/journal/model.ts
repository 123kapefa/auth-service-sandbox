/** Действия журнала — как AuditActions на сервере, по-русски. */
export const ACTION_LABELS: Record<string, string> = {
  UserRestrictionChanged: "Блокировка аккаунта",
  UserRoleChanged: "Роли пользователя",
  UserAppAccessChanged: "Доступ к приложению",
  UserLoggedOutEverywhere: "«Выйти везде»",
  UserPasswordResetRequested: "Ссылка на смену пароля",
  UserTwoFactorDisabled: "Отключение 2FA",
  UserSignInMethodChanged: "Способ входа",
  UserSignInMethodsReset: "Способы входа по умолчанию",
  UserPhoneChanged: "Смена номера",
  PermissionCreated: "Право создано",
  PermissionDeleted: "Право удалено",
  RoleCreated: "Роль создана",
  RoleUpdated: "Роль изменена",
  RoleDeleted: "Роль удалена",
  ProjectBrandingChanged: "Оформление проекта",
  AppBrandingChanged: "Оформление приложения",
  ProjectCodesChanged: "Коды подтверждения",
  ProjectSignInChanged: "Способы входа",
};

export const actionLabel = (action: string) => ACTION_LABELS[action] ?? action;

export const LOGIN_EVENT_LABELS: Record<string, string> = {
  LOGIN: "Вход",
  LOGIN_ERROR: "Ошибка входа",
  LOGOUT: "Выход",
};

const show = (value: unknown): string => {
  if (value === null || value === undefined || value === "") {
    return "—";
  }

  if (Array.isArray(value)) {
    return value.length === 0 ? "—" : value.map(show).join(", ");
  }

  if (typeof value === "boolean") {
    return value ? "да" : "нет";
  }

  return typeof value === "object" ? JSON.stringify(value) : String(value);
};

const isChange = (value: unknown): value is { old: unknown; new: unknown } =>
  typeof value === "object" && value !== null && "old" in value && "new" in value;

/**
 * Изменения одной строкой на поле: «поле: было → стало» для { old, new }, иначе «поле: значение». Приложение не
 * повторяется — оно в отдельной колонке.
 */
export function describeChanges(changes: Record<string, unknown>): string[] {
  return Object.entries(changes)
    .filter(([field]) => field !== "app")
    .map(([field, value]) =>
      isChange(value)
        ? `${field}: ${show(value.old)} → ${show(value.new)}`
        : `${field}: ${show(value)}`,
    );
}
