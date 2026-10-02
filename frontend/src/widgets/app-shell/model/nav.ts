import { type AdminAccess, Permissions } from "@/entities/admin-session";

export interface NavItem {
  to: string;
  label: string;
  /** Подсвечивать только на точном адресе (обзор), иначе — и на вложенных страницах. */
  end?: boolean;
}

/** Разделы в меню — по тому, что вызвавшему доступно (проверяет всё равно сервер). */
export function navItems(access: AdminAccess): NavItem[] {
  const items: NavItem[] = [{ to: "/", label: "Обзор", end: true }];
  if (access.projectUsers) {
    items.push({ to: "/users", label: "Пользователи" });
  }

  // Приложение — в меню, если в нём можно выдавать роли или менять сами роли (раздел с вкладками).
  for (const app of access.apps) {
    if (
      app.permissions.includes(Permissions.usersRolesManage) ||
      app.permissions.includes(Permissions.rolesManage)
    ) {
      items.push({ to: `/apps/${app.clientId}`, label: app.name ?? app.clientId });
    }
  }

  return items;
}
