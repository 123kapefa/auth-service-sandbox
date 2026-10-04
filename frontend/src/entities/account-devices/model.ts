/** Причины ошибки входа (события Keycloak) — по-русски. */
const LOGIN_ERROR_LABELS: Record<string, string> = {
  invalid_user_credentials: "неверный пароль или код",
  user_temporarily_disabled: "много попыток, вход приостановлен",
  user_disabled: "аккаунт заблокирован",
  expired_code: "код истёк",
  invalid_code: "неверный код",
  user_not_found: "пользователь не найден",
  invalid_authentication_session: "вход начат заново",
  access_denied: "доступ закрыт",
};

export function loginErrorLabel(error: string | null): string {
  return error ? (LOGIN_ERROR_LABELS[error] ?? `ошибка (${error})`) : "ошибка";
}

/** Подпись устройства: не узнали — «Неизвестное устройство». */
export function deviceName(label: string | null): string {
  return label ?? "Неизвестное устройство";
}
