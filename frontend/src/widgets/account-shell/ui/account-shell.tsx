import { useMutation } from "@tanstack/react-query";
import { LogOut } from "lucide-react";
import { NavLink, Outlet } from "react-router";
import { accountProfileApi, type AccountProfile } from "@/entities/account-profile";
import { cn } from "@/shared/lib/cn";
import { Button } from "@/shared/ui";
import { accountNav } from "../model/nav";

/** Каркас кабинета: разделы, кто вошёл, выход. */
export function AccountShell({ profile }: { profile: AccountProfile }) {
  const logout = useMutation({
    mutationFn: accountProfileApi.logout,
    onSettled: () => window.location.assign("/"),
  });

  return (
    <div className="min-h-dvh bg-background text-foreground">
      <header className="border-b border-border bg-card">
        <div className="mx-auto flex max-w-3xl flex-wrap items-center gap-4 px-4 py-3">
          <span className="font-semibold">Личный кабинет</span>
          <nav className="flex gap-1" aria-label="Разделы">
            {accountNav.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) =>
                  cn(
                    "rounded-md px-3 py-1.5 text-sm hover:bg-muted",
                    isActive && "bg-muted font-medium",
                  )
                }
              >
                {item.label}
              </NavLink>
            ))}
          </nav>
          <div className="ml-auto flex items-center gap-3 text-sm text-muted-foreground">
            <span>{profile.phone ?? profile.email ?? "—"}</span>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => logout.mutate()}
              disabled={logout.isPending}
            >
              <LogOut className="size-4" aria-hidden /> Выйти
            </Button>
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-3xl px-4 py-6">
        <Outlet />
      </main>
    </div>
  );
}
