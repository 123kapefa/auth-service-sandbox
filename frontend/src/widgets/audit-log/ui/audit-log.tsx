import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router";
import { ACTION_LABELS, actionLabel, describeChanges, journalQueries } from "@/entities/journal";
import { getErrorMessage } from "@/shared/api";
import { formatDateTime } from "@/shared/lib/format";
import { Button, Card, EmptyState, ErrorState, Input, Label, Loading, Pager } from "@/shared/ui";
import { filterFromParams, paramsFromFilter } from "../model/filter";

interface Props {
  /** Журнал одного приложения (для его админа); null — весь проект. */
  clientId: string | null;
  /** Приложения для фильтра (только в журнале проекта). */
  apps?: { clientId: string; name: string | null }[];
}

const selectClass =
  "h-10 border border-border bg-background px-2 text-sm focus-visible:border-primary focus-visible:shadow-hard focus-visible:outline-none";

/** Действия сотрудников: кто, что, с кем, в каком режиме и что изменилось. Новые сверху. */
export function AuditLog({ clientId, apps = [] }: Props) {
  const [params, setParams] = useSearchParams();
  const filter = filterFromParams(params);
  const query = useQuery(journalQueries.audit(clientId, filter));
  const update = (change: Partial<typeof filter>) =>
    setParams(paramsFromFilter({ ...filter, page: 1, ...change }, params));

  return (
    <div className="grid gap-3">
      <div className="flex flex-wrap items-end gap-3">
        <div className="grid gap-1">
          <Label htmlFor="audit-action">Действие</Label>
          <select
            id="audit-action"
            className={selectClass}
            value={filter.action}
            onChange={(event) => update({ action: event.target.value })}
          >
            <option value="">все</option>
            {Object.entries(ACTION_LABELS).map(([action, label]) => (
              <option key={action} value={action}>
                {label}
              </option>
            ))}
          </select>
        </div>
        {clientId === null && (
          <div className="grid gap-1">
            <Label htmlFor="audit-app">Приложение</Label>
            <select
              id="audit-app"
              className={selectClass}
              value={filter.app}
              onChange={(event) => update({ app: event.target.value })}
            >
              <option value="">все</option>
              {apps.map((app) => (
                <option key={app.clientId} value={app.clientId}>
                  {app.name ?? app.clientId}
                </option>
              ))}
            </select>
          </div>
        )}
        <div className="grid gap-1">
          <Label htmlFor="audit-from">С</Label>
          <Input
            id="audit-from"
            type="date"
            className="w-40"
            value={filter.from}
            onChange={(event) => update({ from: event.target.value })}
          />
        </div>
        <div className="grid gap-1">
          <Label htmlFor="audit-to">По</Label>
          <Input
            id="audit-to"
            type="date"
            className="w-40"
            value={filter.to}
            onChange={(event) => update({ to: event.target.value })}
          />
        </div>
        {filter.objectId && (
          <Button variant="outline" size="sm" onClick={() => update({ objectId: "" })}>
            Все объекты (сейчас — один)
          </Button>
        )}
      </div>
      <Card className="overflow-x-auto p-0">
        {query.isPending ? (
          <Loading />
        ) : query.isError ? (
          <ErrorState message={getErrorMessage(query.error, "Не удалось загрузить журнал")} />
        ) : query.data.items.length === 0 ? (
          <EmptyState>Записей нет</EmptyState>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-left font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase">
              <tr className="border-b-2 border-primary">
                <th className="px-4 py-2 font-medium">Когда</th>
                <th className="px-4 py-2 font-medium">Кто</th>
                <th className="px-4 py-2 font-medium">Действие</th>
                <th className="px-4 py-2 font-medium">С кем / с чем</th>
                {clientId === null && <th className="px-4 py-2 font-medium">Приложение</th>}
                <th className="px-4 py-2 font-medium">Изменения</th>
              </tr>
            </thead>
            <tbody>
              {query.data.items.map((entry) => (
                <tr key={entry.id} className="border-b border-border align-top last:border-0">
                  <td className="px-4 py-2 whitespace-nowrap">
                    {formatDateTime(entry.occurredAt)}
                  </td>
                  <td className="px-4 py-2">
                    {entry.actorName ?? "—"}
                    {entry.actorRole && (
                      <span className="block text-xs text-muted-foreground">
                        режим {entry.actorRole}
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-2">{actionLabel(entry.action)}</td>
                  <td className="px-4 py-2">{entry.objectName ?? entry.objectId}</td>
                  {clientId === null && <td className="px-4 py-2">{entry.app ?? "аккаунт"}</td>}
                  <td className="px-4 py-2">
                    {describeChanges(entry.changes).map((line) => (
                      <div key={line}>{line}</div>
                    ))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
      {query.data && (
        <Pager
          page={filter.page}
          hasMore={query.data.hasMore}
          onPage={(page) => setParams(paramsFromFilter({ ...filter, page }, params))}
        />
      )}
    </div>
  );
}
