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

  if (app.permissions.includes(Permissions.auditView)) {
    tabs.push({ to: "journal", label: "Журнал" });
  }

  // Оформление приложения — только у админа проекта (право проекта branding.manage приходит в права приложения).
  if (app.permissions.includes(Permissions.brandingManage)) {
    tabs.push({ to: "branding", label: "Оформление" });
  }

  return tabs;
}
