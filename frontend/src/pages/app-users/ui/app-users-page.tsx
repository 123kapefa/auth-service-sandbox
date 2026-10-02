import { useQuery } from "@tanstack/react-query";
import { Link, useParams, useSearchParams } from "react-router";
import { useManagedApp } from "@/entities/admin-session";
import { appUserQueries } from "@/entities/app-user";
import { getErrorMessage } from "@/shared/api";
import { Card, EmptyState, ErrorState, Loading, Pager, SearchBox } from "@/shared/ui";

/**
 * Пользователи приложения: без поиска — те, у кого есть роль в нём; поиск — точный телефон или почта (найти человека,
 * чтобы выдать ему роль; перебрать базу проекта так нельзя).
 */
export function AppUsersPage() {
  const { clientId = "" } = useParams();
  const app = useManagedApp(clientId);
  const [params, setParams] = useSearchParams();
  const search = params.get("search") ?? "";
  const page = Number(params.get("page") ?? "1") || 1;
  const query = useQuery({ ...appUserQueries.list(clientId, search, page), enabled: app !== null });

  if (!app) {
    return <ErrorState message="Это приложение вам не подчинено." />;
  }

  return (
    <div className="grid gap-4">
      <SearchBox
        value={search}
        label="Найти пользователя"
        placeholder="Точный телефон или почта — выдать роль новому человеку"
        onSearch={(value) => setParams(value ? { search: value } : {})}
      />
      <Card className="overflow-x-auto p-0">
        {query.isPending ? (
          <Loading />
        ) : query.isError ? (
          <ErrorState
            message={getErrorMessage(query.error, "Не удалось загрузить пользователей")}
          />
        ) : query.data.items.length === 0 ? (
          <EmptyState>
            {search ? "Такого пользователя нет" : "Ролей в приложении ещё ни у кого нет"}
          </EmptyState>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-left text-muted-foreground">
              <tr className="border-b border-border">
                <th className="px-4 py-2 font-medium">Пользователь</th>
                <th className="px-4 py-2 font-medium">Почта</th>
                <th className="px-4 py-2 font-medium">Роли</th>
              </tr>
            </thead>
            <tbody>
              {query.data.items.map((user) => (
                <tr
                  key={user.id}
                  className="border-b border-border last:border-0 hover:bg-muted/50"
                >
                  <td className="px-4 py-2">
                    <Link
                      to={`/apps/${clientId}/users/${user.id}`}
                      state={{ title: user.phone ?? user.username }}
                      className="font-medium underline-offset-2 hover:underline"
                    >
                      {user.phone ?? user.username}
                    </Link>
                  </td>
                  <td className="px-4 py-2">{user.email ?? "—"}</td>
                  <td className="px-4 py-2">{user.roles.join(", ") || "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
      {query.data && (
        <Pager
          page={page}
          hasMore={query.data.hasMore}
          onPage={(next) =>
            setParams(search ? { search, page: String(next) } : { page: String(next) })
          }
        />
      )}
    </div>
  );
}
