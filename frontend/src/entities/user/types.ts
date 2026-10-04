/** Строка списка пользователей (GET /auth/admin/users). */
export interface UserListItem {
  id: string;
  username: string;
  phone: string | null;
  email: string | null;
  /** Роли сотрудника через запятую или customer. */
  role: string;
  createdAt: string;
  isRestricted: boolean;
  restrictionReason: string | null;
  restrictedAt: string | null;
}

export interface UsersPage {
  items: UserListItem[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}

/** Карточка (GET /auth/admin/users/{id}). */
export interface UserDetail extends UserListItem {
  availableRoles: string[];
  activeMode: string | null;
  hasPassword: boolean;
  lastLoginAt: string | null;
  hasTwoFactor: boolean;
}

/** Доступ к приложению (GET /auth/admin/users/{id}/apps). */
export interface AppAccess {
  clientId: string;
  name: string | null;
  hasAccess: boolean;
}

/** Сессия (GET /auth/admin/users/{id}/sessions). */
export interface UserSession {
  id: string;
  ipAddress: string | null;
  /** «Chrome · macOS» — браузер, с которого вошли; null — не узнали. */
  deviceLabel: string | null;
  createdAt: string;
  lastSeenAt: string;
  apps: string[];
}
