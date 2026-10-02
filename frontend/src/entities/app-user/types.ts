/** Пользователь приложения (GET /auth/admin/apps/{clientId}/users). */
export interface AppUser {
  id: string;
  username: string;
  phone: string | null;
  email: string | null;
  /** Выданные роли в приложении. */
  roles: string[];
}

export interface AppUsersPage {
  items: AppUser[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}

/** Роли пользователя в приложении (GET /auth/admin/apps/{clientId}/users/{id}/roles). */
export interface UserAppRoles {
  userId: string;
  roles: string[];
  defaultRoles: string[];
  modes: string[];
  activeMode: string | null;
  /** null — доступ к приложению не закрывается. */
  hasAccess: boolean | null;
  assignableRoles: { name: string; description: string | null }[];
}
