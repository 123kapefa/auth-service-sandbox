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

/** Способ входа, который человек может выключить (10.3). */
export type SignInMethodKey = "password" | "phone-code" | "email-code";

/**
 * Почему способ нельзя выключить: способа нет; сначала ключ доступа или приложение-аутентификатор; это последний способ;
 * сначала коды восстановления.
 */
export type DisableBlocker =
  "not_available" | "needs_passkey_or_authenticator" | "last_method" | "recovery_codes_required";

export interface SignInMethod {
  method: SignInMethodKey;
  /** Есть у человека: пароль задан, телефон или почта указаны. */
  available: boolean;
  /** Работает (не выключен). */
  enabled: boolean;
  disableBlocker: DisableBlocker | null;
}

/** Изменение способов входа; у «вернуть по умолчанию» (админ) method и enabled — null. */
export interface SignInMethodChange {
  at: string;
  method: SignInMethodKey | null;
  enabled: boolean | null;
  byAdmin: boolean;
}

/** Способы входа человека (GET /me/sign-in-methods). */
export interface SignInMethods {
  /** Пароль, код на телефон, код на почту — всегда все три. */
  methods: SignInMethod[];
  hasPasskey: boolean;
  hasAuthenticator: boolean;
  hasRecoveryCodes: boolean;
  /** Привязанные внешние входы: yandex, esia. */
  linkedProviders: string[];
  /** Новые сверху. */
  history: SignInMethodChange[];
  /** Выключено в проекте (phone, email, password, passkey, yandex, esia) — в кабинете не показываем. */
  projectDisabled: string[];
}
