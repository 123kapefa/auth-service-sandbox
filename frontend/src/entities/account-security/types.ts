/** Подключённое приложение-аутентификатор или ключ доступа: Id (для удаления), подпись, когда подключено. */
export interface AccountCredential {
  id: string;
  label: string | null;
  createdAt: string;
}

/** Коды восстановления: сколько осталось из скольких (может быть неизвестно). */
export interface RecoveryCodes {
  id: string;
  createdAt: string;
  remaining: number | null;
  total: number | null;
}

/** Раздел «Безопасность» (GET /me/security): без секретов. */
export interface AccountSecurity {
  hasPassword: boolean;
  authenticators: AccountCredential[];
  passkeys: AccountCredential[];
  recoveryCodes: RecoveryCodes | null;
}
