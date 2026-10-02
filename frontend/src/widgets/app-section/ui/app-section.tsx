import { Navigate, NavLink, Outlet, useParams } from "react-router";
import { useManagedApp } from "@/entities/admin-session";
import { cn } from "@/shared/lib/cn";
import { ErrorState } from "@/shared/ui";
import { appTabs } from "../model/tabs";

/** Раздел приложения: название и вкладки (пользователи, роли и права — по правам в нём). */
export function AppSection() {
  const { clientId = "" } = useParams();
  const app = useManagedApp(clientId);

  if (!app) {
    return <ErrorState message="Это приложение вам не подчинено." />;
  }

  const tabs = appTabs(app);
  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-end gap-4 border-b border-border">
        <h1 className="pb-2 text-lg font-semibold">{app.name ?? app.clientId}</h1>
        <nav className="flex gap-1" aria-label="Разделы приложения">
          {tabs.map((tab) => (
            <NavLink
              key={tab.to}
              to={tab.to}
              className={({ isActive }) =>
                cn(
                  "-mb-px border-b-2 border-transparent px-3 pb-2 text-sm hover:text-foreground",
                  isActive ? "border-primary font-medium" : "text-muted-foreground",
                )
              }
            >
              {tab.label}
            </NavLink>
          ))}
        </nav>
      </div>
      <Outlet />
    </div>
  );
}

/** /apps/{clientId} — первая доступная вкладка. */
export function AppSectionIndex() {
  const { clientId = "" } = useParams();
  const app = useManagedApp(clientId);
  const first = app ? appTabs(app)[0] : undefined;
  return first ? (
    <Navigate to={first.to} replace />
  ) : (
    <ErrorState message="В этом приложении вам ничего не доступно." />
  );
}
