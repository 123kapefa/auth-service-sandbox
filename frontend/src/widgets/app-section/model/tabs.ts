import { Permissions, type ManagedApp } from "@/entities/admin-session";

export interface AppTab {
  to: string;
  label: string;
}

/** Вкладки приложения — по правам вызвавшего в нём. */
export function appTabs(app: ManagedApp): AppTab[] {
  const tabs: AppTab[] = [];
  if (app.permissions.includes(Permissions.usersRolesManage)) {
    tabs.push({ to: "users", label: "Пользователи" });
  }

  if (app.permissions.includes(Permissions.rolesManage)) {
    tabs.push({ to: "roles", label: "Роли и права" });
  }

  return tabs;
}
