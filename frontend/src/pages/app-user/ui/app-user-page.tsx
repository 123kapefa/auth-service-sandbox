import { useQuery } from "@tanstack/react-query";
import { Link, useLocation, useParams } from "react-router";
import { Permissions, useManagedApp } from "@/entities/admin-session";
import { appUserQueries } from "@/entities/app-user";
import { AppAccessToggle } from "@/features/user-app-access";
import { UserRolesEditor } from "@/features/user-roles";
import { getErrorMessage } from "@/shared/api";
import { Badge, Card, CardTitle, ErrorState, Loading } from "@/shared/ui";

/** Пользователь в приложении: его роли и доступ к приложению — всё, чем управляет админ приложения. */
export function AppUserPage() {
  const { clientId = "", userId = "" } = useParams();
  const title = (useLocation().state as { title?: string } | null)?.title ?? userId;
  const app = useManagedApp(clientId);
  const query = useQuery({ ...appUserQueries.roles(clientId, userId), enabled: app !== null });

  if (!app) {
    return <ErrorState message="Это приложение вам не подчинено." />;
  }

  const appName = app.name ?? app.clientId;
  return (
    <div className="grid gap-4">
      <div>
        <Link
          to={`/apps/${clientId}/users`}
          className="text-sm text-muted-foreground hover:underline"
        >
          ← Пользователи
        </Link>
        <h2 className="mt-1 text-base font-semibold">{title}</h2>
      </div>
      {query.isPending ? (
        <Loading />
      ) : query.isError ? (
        <ErrorState message={getErrorMessage(query.error, "Не удалось загрузить роли")} />
      ) : (
        <>
          <Card>
            <CardTitle>Роли в приложении</CardTitle>
            {app.permissions.includes(Permissions.usersRolesManage) ? (
              <UserRolesEditor
                key={query.data.roles.join(",")}
                clientId={clientId}
                data={query.data}
              />
            ) : (
              <p className="text-sm">{query.data.roles.join(", ") || "—"}</p>
            )}
          </Card>
          {query.data.hasAccess !== null && (
            <Card>
              <CardTitle>Доступ к приложению</CardTitle>
              <div className="flex items-center justify-between gap-3 text-sm">
                {query.data.hasAccess ? (
                  <Badge variant="solid">открыт</Badge>
                ) : (
                  <Badge variant="warning">закрыт</Badge>
                )}
                {app.permissions.includes(Permissions.usersBlock) && (
                  <AppAccessToggle
                    userId={userId}
                    clientId={clientId}
                    appName={appName}
                    hasAccess={query.data.hasAccess}
                  />
                )}
              </div>
            </Card>
          )}
        </>
      )}
    </div>
  );
}
