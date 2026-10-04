/** GET /auth/admin/me — с чего начинается админка. */
export interface ManagedApp {
  clientId: string;
  name: string | null;
  /** roles.manage, users.roles.manage, users.block, audit.view — какие есть у вызвавшего в этом приложении. */
  permissions: string[];
  /** true — права админа проекта (из токена), false — админ самого приложения. */
  viaProject: boolean;
}

export interface AdminSession {
  id: string;
  phone: string | null;
  activeMode: string | null;
  /** Права активного режима — уровень проекта. */
  permissions: string[];
  apps: ManagedApp[];
}
