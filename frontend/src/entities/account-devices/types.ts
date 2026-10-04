/** Сессия — где вы вошли (GET /security/sessions). */
export interface AccountSession {
  id: string;
  userAgent: string | null;
  ipAddress: string | null;
  /** «Chrome · macOS»; null — устройство не узнали (вход мимо сервиса или старый). */
  deviceLabel: string | null;
  createdAt: string;
  lastSeenAt: string;
  isCurrent: boolean;
  apps: string[];
}

/** Попытка входа за 30 дней (GET /security/logins), новые сверху. */
export interface LoginAttempt {
  time: string;
  success: boolean;
  /** Причина ошибки от сервера входа (invalid_user_credentials…). */
  error: string | null;
  ipAddress: string | null;
  deviceLabel: string | null;
  sessionId: string | null;
}
