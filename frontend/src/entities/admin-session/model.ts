import { type AdminSession } from "./types";

/** Права сервиса входа, от которых зависят разделы админки (AuthServicePermissions на сервере). */
export const Permissions = {
  usersView: "users.view",
  usersBlock: "users.block",
  usersRolesManage: "users.roles.manage",
  usersSessionsManage: "users.sessions.manage",
  usersCredentialsManage: "users.credentials.manage",
  auditView: "audit.view",
  rolesManage: "roles.manage",
} as const;

/** Что вызвавшему видно в админке. Только для интерфейса — доступ проверяет сервер. */
export function accessOf(session: AdminSession) {
  const project = new Set(session.permissions);
  return {
    /** Пользователи проекта: поиск, карточка, блокировка… */
    projectUsers: project.has(Permissions.usersView),
    /** Журнал всего проекта. */
    projectAudit: project.has(Permissions.auditView),
    /** Приложения под управлением (у админа приложения — только своё). */
    apps: session.apps,
    /** Есть ли что делать в админке вообще. */
    any:
      project.has(Permissions.usersView) ||
      project.has(Permissions.auditView) ||
      session.apps.length > 0,
  };
}

export type AdminAccess = ReturnType<typeof accessOf>;
