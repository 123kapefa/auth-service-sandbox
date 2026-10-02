/** Право приложения (GET /auth/admin/apps/{clientId}/permissions). */
export interface AppPermission {
  name: string;
  description: string | null;
  /** code — объявлено кодом сервиса (не удалить), admin — создано в админке. */
  source: string | null;
}

/** Роль (режим) приложения и её права (GET /auth/admin/apps/{clientId}/roles). */
export interface AppRole {
  name: string;
  description: string | null;
  /** Встроенная (customer, manager, admin…) — не удалить. */
  builtin: boolean;
  permissions: string[];
}

export interface RoleDraft {
  name: string;
  description: string;
  permissions: string[];
}
