import { useSuspenseQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { accessOf, adminSessionQueries } from "@/entities/admin-session";
import { Badge, Card, CardTitle } from "@/shared/ui";
import { appTabs } from "@/widgets/app-section";

/** Обзор: что вызвавшему доступно — уровень проекта и приложения под управлением. */
export function OverviewPage() {
  const { data: session } = useSuspenseQuery(adminSessionQueries.me());
  const access = accessOf(session);

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <Card>
        <CardTitle>Проект</CardTitle>
        {access.projectUsers || access.projectAudit ? (
          <ul className="flex flex-wrap gap-2">
            {session.permissions.map((permission) => (
              <li key={permission}>
                <Badge>{permission}</Badge>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-muted-foreground">
            Прав уровня проекта нет — только свои приложения.
          </p>
        )}
      </Card>
      <Card>
        <CardTitle>Приложения</CardTitle>
        {access.apps.length === 0 ? (
          <p className="text-sm text-muted-foreground">Приложений под управлением нет.</p>
        ) : (
          <ul className="grid gap-3">
            {access.apps.map((app) => (
              <li key={app.clientId}>
                {appTabs(app).length > 0 ? (
                  <Link
                    to={`/apps/${app.clientId}`}
                    className="font-medium underline-offset-2 hover:underline"
                  >
                    {app.name ?? app.clientId}
                  </Link>
                ) : (
                  <div className="font-medium">{app.name ?? app.clientId}</div>
                )}
                <div className="text-xs text-muted-foreground">
                  {app.clientId} · {app.viaProject ? "как админ проекта" : "как админ приложения"}
                </div>
                <div className="mt-1 flex flex-wrap gap-1">
                  {app.permissions.map((permission) => (
                    <Badge key={permission}>{permission}</Badge>
                  ))}
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}
