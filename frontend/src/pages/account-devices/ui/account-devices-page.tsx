import { useQuery, useSuspenseQuery } from "@tanstack/react-query";
import {
  type AccountSession,
  accountDevicesQueries,
  deviceName,
  loginErrorLabel,
} from "@/entities/account-devices";
import {
  LogoutEverywhereButton,
  LogoutOthersButton,
  TerminateSessionButton,
} from "@/features/account-sessions";
import { getErrorMessage } from "@/shared/api";
import { formatAgo, formatDateTime } from "@/shared/lib/format";
import { Card, CardTitle, EmptyState, ErrorState, Loading } from "@/shared/ui";

/**
 * Устройства и входы: где вы сейчас вошли (выйти на устройстве, на всех других, везде) и история входов за 30 дней —
 * удачных и нет, с устройством и IP. Устройство сервис узнаёт по браузеру при входе.
 */
export function AccountDevicesPage() {
  const { data: sessions } = useSuspenseQuery(accountDevicesQueries.sessions());
  const hasOthers = sessions.some((session) => !session.isCurrent);

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="mr-auto text-3xl font-bold tracking-tight">Устройства и входы</h1>
        <LogoutOthersButton disabled={!hasOthers} />
        <LogoutEverywhereButton />
      </div>

      <Card aria-label="Где вы вошли">
        <CardTitle>Где вы вошли</CardTitle>
        {sessions.length === 0 ? (
          <EmptyState>Активных сессий нет</EmptyState>
        ) : (
          <ul className="border-t-2 border-primary">
            {sessions.map((session, index) => (
              <SessionRow key={session.id} session={session} number={index + 1} />
            ))}
          </ul>
        )}
      </Card>

      <LoginHistory />
    </div>
  );
}

function SessionRow({ session, number }: { session: AccountSession; number: number }) {
  const device = deviceName(session.deviceLabel);
  return (
    <li className="flex flex-wrap items-center gap-4 border-b border-border py-3.5 last:border-0">
      <span className="w-6 font-mono text-xs text-muted-foreground">
        {String(number).padStart(2, "0")}
      </span>
      <div className="flex min-w-0 flex-col gap-0.5">
        <span className="font-semibold">{device}</span>
        <span className="font-mono text-xs text-muted-foreground">
          {session.ipAddress ?? "IP неизвестен"} ·{" "}
          {session.isCurrent ? "активна сейчас" : `активность ${formatAgo(session.lastSeenAt)}`} ·
          вход {formatDateTime(session.createdAt)}
        </span>
      </div>
      <div className="ml-auto">
        {session.isCurrent ? (
          <span className="font-mono text-xs text-muted-foreground">[ это устройство ]</span>
        ) : (
          <TerminateSessionButton sessionId={session.id} device={device} />
        )}
      </div>
    </li>
  );
}

/** История — отдельным запросом: сервер входа недоступен — сессии всё равно видны. */
function LoginHistory() {
  const query = useQuery(accountDevicesQueries.logins());

  return (
    <Card aria-label="История входов" className="overflow-x-auto">
      <CardTitle>История входов · 30 дней</CardTitle>
      {query.isPending ? (
        <Loading />
      ) : query.isError ? (
        <ErrorState message={getErrorMessage(query.error, "Не удалось загрузить историю входов")} />
      ) : query.data.length === 0 ? (
        <EmptyState>Входов не было</EmptyState>
      ) : (
        <table className="w-full text-sm">
          <thead className="text-left font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase">
            <tr className="border-b-2 border-primary">
              <th className="py-2.5 pr-3 font-medium">Когда</th>
              <th className="py-2.5 pr-3 font-medium">Устройство</th>
              <th className="py-2.5 pr-3 font-medium">IP</th>
              <th className="py-2.5 font-medium">Итог</th>
            </tr>
          </thead>
          <tbody>
            {query.data.map((attempt, index) => (
              <tr
                key={`${attempt.time}-${index}`}
                className="border-b border-border align-top last:border-0"
              >
                <td className="py-3 pr-3 font-mono text-[13px] whitespace-nowrap">
                  {formatDateTime(attempt.time)}
                </td>
                <td className="py-3 pr-3">
                  {attempt.success ? deviceName(attempt.deviceLabel) : "—"}
                </td>
                <td className="py-3 pr-3 font-mono text-[13px]">{attempt.ipAddress ?? "—"}</td>
                <td className="py-3">
                  {attempt.success ? (
                    "успешно"
                  ) : (
                    <span className="inline-flex items-center gap-1.5 bg-primary px-2 py-0.5 text-[13px] text-primary-foreground">
                      <span className="font-mono font-bold" aria-hidden>
                        !
                      </span>
                      {loginErrorLabel(attempt.error)}
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Card>
  );
}
