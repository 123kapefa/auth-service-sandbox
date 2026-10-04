import { useMutation } from "@tanstack/react-query";
import { Outlet, useMatch } from "react-router";
import { accessOf, adminSessionApi, type AdminSession } from "@/entities/admin-session";
import {
  Badge,
  Button,
  Sidebar,
  SidebarGroup,
  SidebarLayout,
  SidebarLink,
  SidebarSubLink,
  ThemeToggle,
} from "@/shared/ui";
import { appTabs } from "@/widgets/app-section";
import { navItems } from "../model/nav";

/** Каркас админки: боковое меню (проект, приложения и разделы открытого), кто вошёл, режим, тема, выход. */
export function AppShell({ session }: { session: AdminSession }) {
  const logout = useMutation({
    mutationFn: adminSessionApi.logout,
    onSettled: () => window.location.assign("/"),
  });
  const openApp = useMatch("/apps/:clientId/*")?.params.clientId;
  const items = navItems(accessOf(session));
  const project = items.filter((item) => !item.app);
  const apps = items.filter((item) => item.app);

  return (
    <SidebarLayout
      sidebar={
        <Sidebar
          title="Админка доступа"
          footer={
            <>
              <span className="font-mono text-[13px]">{session.phone ?? "—"}</span>
              <Badge className="self-start">режим: {session.activeMode ?? "—"}</Badge>
              <div className="flex gap-2">
                <ThemeToggle />
                <Button
                  variant="outline"
                  className="flex-1"
                  onClick={() => logout.mutate()}
                  disabled={logout.isPending}
                >
                  Выйти
                </Button>
              </div>
            </>
          }
        >
          <SidebarGroup label="Проект">
            {project.map((item, index) => (
              <SidebarLink key={item.to} to={item.to} end={item.end} number={index + 1}>
                {item.label}
              </SidebarLink>
            ))}
          </SidebarGroup>
          {apps.length > 0 && (
            <SidebarGroup label="Приложения">
              {apps.map((item, index) => (
                <SidebarLink
                  key={item.to}
                  to={item.to}
                  number={project.length + index + 1}
                  sub={
                    item.app && item.app.clientId === openApp
                      ? appTabs(item.app).map((tab) => (
                          <SidebarSubLink key={tab.to} to={`${item.to}/${tab.to}`}>
                            {tab.label}
                          </SidebarSubLink>
                        ))
                      : undefined
                  }
                >
                  {item.label}
                </SidebarLink>
              ))}
            </SidebarGroup>
          )}
        </Sidebar>
      }
    >
      <Outlet />
    </SidebarLayout>
  );
}
