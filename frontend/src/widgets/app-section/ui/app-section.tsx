import { Navigate, Outlet, useParams } from "react-router";
import { useManagedApp } from "@/entities/admin-session";
import { ErrorState } from "@/shared/ui";
import { appTabs } from "../model/tabs";

/** Раздел приложения: название; его вкладки (пользователи, роли и права, журнал) — в боковом меню под приложением. */
export function AppSection() {
  const { clientId = "" } = useParams();
  const app = useManagedApp(clientId);

  if (!app) {
    return <ErrorState message="Это приложение вам не подчинено." />;
  }

  return (
    <div className="grid gap-6">
      <div className="flex flex-wrap items-baseline gap-3">
        <h1 className="text-3xl font-bold tracking-tight">{app.name ?? app.clientId}</h1>
        <span className="font-mono text-[13px] text-muted-foreground">{app.clientId}</span>
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
