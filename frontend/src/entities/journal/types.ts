/** Запись журнала действий сотрудников (GET /auth/admin/audit). */
export interface AuditEntry {
  id: string;
  occurredAt: string;
  action: string;
  objectType: string;
  objectId: string;
  objectName: string | null;
  /** { "поле": { "old", "new" } } и подробности (app, reason, email…). */
  changes: Record<string, unknown>;
  actorId: string | null;
  /** Телефон сотрудника. */
  actorName: string | null;
  /** Режим, в котором он действовал. */
  actorRole: string | null;
  correlationId: string | null;
  /** Приложение, которого касается действие; null — весь аккаунт. */
  app: string | null;
}

export interface AuditPage {
  items: AuditEntry[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}

/** Фильтры журнала; пустые — не применяются. Даты — YYYY-MM-DD (с начала «с» и до конца «по»). */
export interface AuditFilter {
  action: string;
  app: string;
  from: string;
  to: string;
  objectId: string;
  page: number;
}

/** Вход, ошибка входа или выход (события Keycloak, GET /auth/admin/login-events). */
export interface LoginEvent {
  time: string;
  type: string;
  userId: string | null;
  username: string | null;
  ipAddress: string | null;
  clientId: string | null;
  error: string | null;
}

export interface LoginEventsPage {
  items: LoginEvent[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}
