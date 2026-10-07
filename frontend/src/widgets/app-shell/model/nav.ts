import { type AdminAccess, type ManagedApp } from "@/entities/admin-session";
import { appTabs } from "@/widgets/app-section";

export interface NavItem {
  to: string;
  label: string;
  /** Подсвечивать только на точном адресе (обзор), иначе — и на вложенных страницах. */
  end?: boolean;
  /** Пункт приложения: в меню — в блоке «Приложения», под ним — его разделы. */
  app?: ManagedApp;
}

/** Разделы в меню — по тому, что вызвавшему доступно (проверяет всё равно сервер). */
export function navItems(access: AdminAccess): NavItem[] {
  const items: NavItem[] = [{ to: "/", label: "Обзор", end: true }];
  if (access.projectUsers) {
    items.push({ to: "/users", label: "Пользователи" });
  }

  if (access.projectAudit) {
    items.push({ to: "/journal", label: "Журнал" });
  }

  if (access.projectServices) {
    items.push({ to: "/services", label: "Сервисы" });
  }

  if (access.projectBranding) {
    items.push({ to: "/branding", label: "Оформление" });
  }

  if (access.projectSignIn) {
    items.push({ to: "/sign-in", label: "Вход и коды" });
  }

  // Приложение — в меню, если в его разделе есть хоть одна вкладка (выдача ролей, роли и права, журнал).
  for (const app of access.apps) {
    if (appTabs(app).length > 0) {
      items.push({ to: `/apps/${app.clientId}`, label: app.name ?? app.clientId, app });
    }
  }

  return items;
}
