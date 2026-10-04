import { useSuspenseQuery } from "@tanstack/react-query";
import { accessOf, adminSessionQueries } from "@/entities/admin-session";
import { NoAccessPage } from "@/pages/no-access";
import { AppShell } from "@/widgets/app-shell";

/** Сессия есть (401 уже увёл на вход): управлять нечем — «нет доступа», иначе каркас с разделами. */
export function SessionGate() {
  const { data: session } = useSuspenseQuery(adminSessionQueries.me());
  return accessOf(session).any ? (
    <AppShell session={session} />
  ) : (
    <NoAccessPage activeMode={session.activeMode} />
  );
}
