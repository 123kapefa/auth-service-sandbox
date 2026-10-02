import { useQuery, useSuspenseQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router";
import { adminSessionQueries, Permissions } from "@/entities/admin-session";
import { userQueries } from "@/entities/user";
import { CredentialsActions } from "@/features/user-credentials";
import { RestrictionControl } from "@/features/user-restriction";
import { getErrorMessage } from "@/shared/api";
import { formatDateTime } from "@/shared/lib/format";
import { Badge, Card, CardTitle, ErrorState, Loading } from "@/shared/ui";
import { AppAccessSection, AppRolesSection, SessionsSection } from "./user-sections";

/**
 * Карточка пользователя для админа проекта (и менеджера — только покупатели). Блоки — по правам вызвавшего:
 * блокировка и доступ — users.block, роли — users.roles.manage в приложении, «выйти везде» — users.sessions.manage,
 * пароль и 2FA — users.credentials.manage.
 */
export function UserPage() {
  const { userId = "" } = useParams();
  const { data: session } = useSuspenseQuery(adminSessionQueries.me());
  const query = useQuery(userQueries.detail(userId));
  const can = new Set(session.permissions);
  const roleApps = session.apps.filter((app) =>
    app.permissions.includes(Permissions.usersRolesManage),
  );

  if (query.isPending) {
    return <Loading />;
  }

  if (query.isError) {
    return (
      <ErrorState message={getErrorMessage(query.error, "Не удалось загрузить пользователя")} />
    );
  }

  const user = query.data;
  return (
    <div className="grid gap-4">
      <div>
        <Link to="/users" className="text-sm text-muted-foreground hover:underline">
          ← Пользователи
        </Link>
        <h1 className="mt-1 text-lg font-semibold">{user.phone ?? user.username}</h1>
      </div>
      <Card>
        <CardTitle>Аккаунт</CardTitle>
        <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-1 text-sm">
          <dt className="text-muted-foreground">Почта</dt>
          <dd>{user.email ?? "—"}</dd>
          <dt className="text-muted-foreground">Роль</dt>
          <dd>{user.role}</dd>
          <dt className="text-muted-foreground">Режимы</dt>
          <dd>
            {user.availableRoles.join(", ")} · активный: {user.activeMode ?? "—"}
          </dd>
          <dt className="text-muted-foreground">Создан</dt>
          <dd>{formatDateTime(user.createdAt)}</dd>
          <dt className="text-muted-foreground">Последний вход</dt>
          <dd>{formatDateTime(user.lastLoginAt)}</dd>
          <dt className="text-muted-foreground">Пароль · 2FA</dt>
          <dd>
            {user.hasPassword ? "задан" : "нет"} · {user.hasTwoFactor ? "включена" : "нет"}
          </dd>
          <dt className="text-muted-foreground">Статус</dt>
          <dd>
            {user.isRestricted ? (
              <>
                <Badge className="border-destructive text-destructive">заблокирован</Badge>{" "}
                {formatDateTime(user.restrictedAt)}
                {user.restrictionReason && ` — ${user.restrictionReason}`}
              </>
            ) : (
              "активен"
            )}
          </dd>
        </dl>
        {can.has(Permissions.usersBlock) && (
          <div className="mt-4">
            <RestrictionControl user={user} />
          </div>
        )}
      </Card>
      {roleApps.map((app) => (
        <AppRolesSection key={app.clientId} app={app} userId={user.id} />
      ))}
      <AppAccessSection userId={user.id} canChange={can.has(Permissions.usersBlock)} />
      <SessionsSection userId={user.id} canLogout={can.has(Permissions.usersSessionsManage)} />
      {can.has(Permissions.usersCredentialsManage) && (
        <Card>
          <CardTitle>Пароль и 2FA</CardTitle>
          <CredentialsActions user={user} />
        </Card>
      )}
    </div>
  );
}
