import { useQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router";
import { userQueries } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { formatDateTime } from "@/shared/lib/format";
import { Badge, Card, EmptyState, ErrorState, Loading, Pager, SearchBox } from "@/shared/ui";

/** Пользователи проекта: поиск по телефону («как набрали»), почте, логину. Менеджер видит только покупателей. */
export function UsersPage() {
  const [params, setParams] = useSearchParams();
  const search = params.get("search") ?? "";
  const page = Number(params.get("page") ?? "1") || 1;
  const query = useQuery(userQueries.list({ search, page }));

  const go = (next: { search?: string; page?: number }) => {
    const values = { search: next.search ?? search, page: String(next.page ?? 1) };
    setParams(values.search ? values : { page: values.page });
  };

  return (
    <div className="grid gap-4">
      <h1 className="text-3xl font-bold tracking-tight">Пользователи</h1>
      <SearchBox
        value={search}
        label="Поиск пользователей"
        placeholder="Телефон, почта или логин"
        onSearch={(value) => go({ search: value, page: 1 })}
      />
      <Card className="overflow-x-auto p-0">
        {query.isPending ? (
          <Loading />
        ) : query.isError ? (
          <ErrorState
            message={getErrorMessage(query.error, "Не удалось загрузить пользователей")}
          />
        ) : query.data.items.length === 0 ? (
          <EmptyState>Никого не нашли</EmptyState>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-left font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase">
              <tr className="border-b-2 border-primary">
                <th className="px-4 py-2 font-medium">Логин</th>
                <th className="px-4 py-2 font-medium">Почта</th>
                <th className="px-4 py-2 font-medium">Роль</th>
                <th className="px-4 py-2 font-medium">Создан</th>
                <th className="px-4 py-2 font-medium">Статус</th>
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
                      to={`/users/${user.id}`}
                      className="font-medium underline-offset-2 hover:underline"
                    >
                      {user.phone ?? user.username}
                    </Link>
                  </td>
                  <td className="px-4 py-2">{user.email ?? "—"}</td>
                  <td className="px-4 py-2">{user.role}</td>
                  <td className="px-4 py-2">{formatDateTime(user.createdAt)}</td>
                  <td className="px-4 py-2">
                    {user.isRestricted ? <Badge variant="warning">заблокирован</Badge> : "активен"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
      {query.data && (
        <Pager page={page} hasMore={query.data.hasMore} onPage={(next) => go({ page: next })} />
      )}
    </div>
  );
}
