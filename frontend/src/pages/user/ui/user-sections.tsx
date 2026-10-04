import { useQuery } from "@tanstack/react-query";
import { appUserQueries } from "@/entities/app-user";
import { type ManagedApp } from "@/entities/admin-session";
import { userQueries } from "@/entities/user";
import { AppAccessToggle } from "@/features/user-app-access";
import { UserRolesEditor } from "@/features/user-roles";
import { LogoutEverywhereButton } from "@/features/user-sessions";
import { getErrorMessage } from "@/shared/api";
import { formatDateTime } from "@/shared/lib/format";
import { Badge, Card, CardTitle, EmptyState, ErrorState, Loading } from "@/shared/ui";

/** Роли пользователя в одном приложении. */
export function AppRolesSection({ app, userId }: { app: ManagedApp; userId: string }) {
  const query = useQuery(appUserQueries.roles(app.clientId, userId));
  return (
    <Card>
      <CardTitle>Роли — {app.name ?? app.clientId}</CardTitle>
      {query.isPending ? (
        <Loading />
      ) : query.isError ? (
        <ErrorState message={getErrorMessage(query.error, "Не удалось загрузить роли")} />
      ) : (
        <UserRolesEditor
          key={query.data.roles.join(",")}
          clientId={app.clientId}
          data={query.data}
        />
      )}
    </Card>
  );
}

/** Доступ к приложениям проекта: закрыть одно, не трогая аккаунт. */
export function AppAccessSection({ userId, canChange }: { userId: string; canChange: boolean }) {
  const query = useQuery(userQueries.apps(userId));
  return (
    <Card>
      <CardTitle>Доступ к приложениям</CardTitle>
      {query.isPending ? (
        <Loading />
      ) : query.isError ? (
        <ErrorState message={getErrorMessage(query.error, "Не удалось загрузить приложения")} />
      ) : (
        <ul className="grid gap-2">
          {query.data.map((app) => (
            <li key={app.clientId} className="flex items-center justify-between gap-3 text-sm">
              <span>
                {app.name ?? app.clientId}{" "}
                {app.hasAccess ? (
                  <Badge variant="solid">открыт</Badge>
                ) : (
                  <Badge variant="warning">закрыт</Badge>
                )}
              </span>
              {canChange && (
                <AppAccessToggle
                  userId={userId}
                  clientId={app.clientId}
                  appName={app.name ?? app.clientId}
                  hasAccess={app.hasAccess}
                />
              )}
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

/** Сессии пользователя (входы на устройствах) и «выйти везде». */
export function SessionsSection({ userId, canLogout }: { userId: string; canLogout: boolean }) {
  const query = useQuery(userQueries.sessions(userId));
  return (
    <Card>
      <div className="mb-3 flex items-center justify-between">
        <CardTitle className="mb-0">Сессии</CardTitle>
        {canLogout && <LogoutEverywhereButton userId={userId} disabled={!query.data?.length} />}
      </div>
      {query.isPending ? (
        <Loading />
      ) : query.isError ? (
        <ErrorState message={getErrorMessage(query.error, "Не удалось загрузить сессии")} />
      ) : query.data.length === 0 ? (
        <EmptyState>Открытых сессий нет</EmptyState>
      ) : (
        <ul className="grid gap-2 text-sm">
          {query.data.map((session) => (
            <li key={session.id} className="flex flex-wrap justify-between gap-2">
              <span>
                {session.deviceLabel ?? "Неизвестное устройство"} · {session.ipAddress ?? "—"} ·{" "}
                {session.apps.join(", ") || "—"}
              </span>
              <span className="text-muted-foreground">
                вход {formatDateTime(session.createdAt)}, активность{" "}
                {formatDateTime(session.lastSeenAt)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
