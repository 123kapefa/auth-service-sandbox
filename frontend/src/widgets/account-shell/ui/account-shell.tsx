import { useMutation } from "@tanstack/react-query";
import { Outlet } from "react-router";
import { accountProfileApi, type AccountProfile } from "@/entities/account-profile";
import {
  Button,
  Sidebar,
  SidebarGroup,
  SidebarLayout,
  SidebarLink,
  ThemeToggle,
} from "@/shared/ui";
import { accountNav } from "../model/nav";

/** Каркас кабинета: боковое меню разделов, кто вошёл, тема, выход. */
export function AccountShell({ profile }: { profile: AccountProfile }) {
  const logout = useMutation({
    mutationFn: accountProfileApi.logout,
    onSettled: () => window.location.assign("/"),
  });

  return (
    <SidebarLayout
      sidebar={
        <Sidebar
          title="Личный кабинет"
          footer={
            <>
              <span className="font-mono text-[13px]">{profile.phone ?? profile.email ?? "—"}</span>
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
          <SidebarGroup label="Аккаунт">
            {accountNav.map((item, index) => (
              <SidebarLink key={item.to} to={item.to} end={item.end} number={index + 1}>
                {item.label}
              </SidebarLink>
            ))}
          </SidebarGroup>
        </Sidebar>
      }
    >
      <Outlet />
    </SidebarLayout>
  );
}
