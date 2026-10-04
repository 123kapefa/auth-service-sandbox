import { useMutation, useQuery } from "@tanstack/react-query";
import { useEffect } from "react";
import { Outlet } from "react-router";
import { accountProfileApi, type AccountProfile } from "@/entities/account-profile";
import { applyBrandingAccent, brandingQueries } from "@/entities/branding";
import { accountApp } from "@/shared/lib/account-app";
import { setDefaultTheme } from "@/shared/lib/theme";
import {
  Button,
  Sidebar,
  SidebarGroup,
  SidebarLayout,
  SidebarLink,
  ThemeToggle,
} from "@/shared/ui";
import { accountNav } from "../model/nav";

/**
 * Каркас кабинета: боковое меню разделов, кто вошёл, тема, выход. Оформление — проекта (или приложения, из которого
 * кабинет открыт): название и логотип в меню, акцент (свой для светлой и тёмной темы) — в кнопки и активный пункт, тема проекта — пока пользователь не выбрал свою.
 * Не загрузилось — серый кабинет, как раньше.
 */
export function AccountShell({ profile }: { profile: AccountProfile }) {
  const { data: branding } = useQuery(brandingQueries.mine(accountApp()));
  useEffect(() => {
    if (!branding) {
      return;
    }

    applyBrandingAccent(branding);
    setDefaultTheme(branding.theme === "system" ? null : branding.theme);
    document.title = `Личный кабинет — ${branding.name}`;
  }, [branding]);

  const logout = useMutation({
    mutationFn: accountProfileApi.logout,
    onSettled: () => window.location.assign("/"),
  });

  return (
    <SidebarLayout
      sidebar={
        <Sidebar
          title={branding?.name ?? "Личный кабинет"}
          subtitle={branding ? "Личный кабинет" : undefined}
          logo={branding?.logo}
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
