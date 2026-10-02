import { useSuspenseQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router";
import { adminSessionQueries } from "@/entities/admin-session";
import { cn } from "@/shared/lib/cn";
import { AuditLog } from "@/widgets/audit-log";
import { LoginEvents } from "./login-events";

/** Журнал проекта: действия сотрудников (своя таблица) и входы (события Keycloak). Право audit.view. */
export function JournalPage() {
  const { data: session } = useSuspenseQuery(adminSessionQueries.me());
  const [params] = useSearchParams();
  const tab = params.get("tab") === "logins" ? "logins" : "actions";
  const tabClass = (active: boolean) =>
    cn(
      "-mb-px border-b-2 border-transparent px-3 pb-2 text-sm",
      active ? "border-primary font-medium" : "text-muted-foreground hover:text-foreground",
    );

  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-end gap-4 border-b border-border">
        <h1 className="pb-2 text-lg font-semibold">Журнал</h1>
        <nav className="flex gap-1" aria-label="Журнал">
          <Link to="?tab=actions" className={tabClass(tab === "actions")}>
            Действия
          </Link>
          <Link to="?tab=logins" className={tabClass(tab === "logins")}>
            Входы
          </Link>
        </nav>
      </div>
      {tab === "actions" ? <AuditLog clientId={null} apps={session.apps} /> : <LoginEvents />}
    </div>
  );
}
