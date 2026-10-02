import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router";
import { journalQueries, LOGIN_EVENT_LABELS } from "@/entities/journal";
import { getErrorMessage } from "@/shared/api";
import { formatDateTime } from "@/shared/lib/format";
import { Button, Card, EmptyState, ErrorState, Loading, Pager } from "@/shared/ui";

/** Входы, ошибки входа (с причиной) и выходы — события Keycloak, хранятся 30 дней. */
export function LoginEvents() {
  const [params, setParams] = useSearchParams();
  const userId = params.get("userId") ?? "";
  const page = Number(params.get("page") ?? "1") || 1;
  const query = useQuery(journalQueries.loginEvents(userId, page));

  const go = (change: { userId?: string; page?: number }) => {
    const next = new URLSearchParams(params);
    const nextUser = change.userId ?? userId;
    if (nextUser) {
      next.set("userId", nextUser);
    } else {
      next.delete("userId");
    }

    next.set("page", String(change.page ?? 1));
    setParams(next);
  };

  return (
    <div className="grid gap-3">
      {userId && (
        <div>
          <Button variant="outline" size="sm" onClick={() => go({ userId: "" })}>
            Все пользователи (сейчас — один)
          </Button>
        </div>
      )}
      <Card className="overflow-x-auto p-0">
        {query.isPending ? (
          <Loading />
        ) : query.isError ? (
          <ErrorState message={getErrorMessage(query.error, "Не удалось загрузить входы")} />
        ) : query.data.items.length === 0 ? (
          <EmptyState>Событий нет</EmptyState>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-left text-muted-foreground">
              <tr className="border-b border-border">
                <th className="px-4 py-2 font-medium">Когда</th>
                <th className="px-4 py-2 font-medium">Событие</th>
                <th className="px-4 py-2 font-medium">Пользователь</th>
                <th className="px-4 py-2 font-medium">Приложение</th>
                <th className="px-4 py-2 font-medium">IP</th>
                <th className="px-4 py-2 font-medium">Причина</th>
              </tr>
            </thead>
            <tbody>
              {query.data.items.map((event, index) => (
                <tr key={`${event.time}-${index}`} className="border-b border-border last:border-0">
                  <td className="px-4 py-2 whitespace-nowrap">{formatDateTime(event.time)}</td>
                  <td className="px-4 py-2">{LOGIN_EVENT_LABELS[event.type] ?? event.type}</td>
                  <td className="px-4 py-2">
                    {event.userId ? (
                      <button
                        type="button"
                        className="underline-offset-2 hover:underline"
                        onClick={() => go({ userId: event.userId ?? "" })}
                      >
                        {event.username ?? event.userId}
                      </button>
                    ) : (
                      (event.username ?? "—")
                    )}
                  </td>
                  <td className="px-4 py-2">{event.clientId ?? "—"}</td>
                  <td className="px-4 py-2">{event.ipAddress ?? "—"}</td>
                  <td className="px-4 py-2">{event.error ?? ""}</td>
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
