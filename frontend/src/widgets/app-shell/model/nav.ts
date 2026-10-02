import { type AdminAccess } from "@/entities/admin-session";

export interface NavItem {
  to: string;
  label: string;
}

/** Разделы в меню — по тому, что вызвавшему доступно. Разделы появляются по шагам этапа 4б. */
export function navItems(_access: AdminAccess): NavItem[] {
  return [{ to: "/", label: "Обзор" }];
}
